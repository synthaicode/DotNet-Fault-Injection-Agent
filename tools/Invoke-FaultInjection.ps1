#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Target,
    [string] $Plan,
    [string] $PlanTarget,
    [string[]] $TargetArguments = @(),
    [string] $RuntimeDirectory = "$PSScriptRoot/../src/Native/Windows/bin",
    [ValidateRange(1, 3600)][int] $TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This launcher supports Windows x64 targets.' }
$targetPath = (Resolve-Path -LiteralPath $Target).ProviderPath
if ([IO.Path]::GetExtension($targetPath) -ine '.exe') { throw 'Target must be an executable.' }
$coreClr = Test-Path -LiteralPath ([IO.Path]::ChangeExtension($targetPath, '.runtimeconfig.json'))
$binary = if ($PlanTarget) { (Resolve-Path -LiteralPath $PlanTarget).ProviderPath }
    elseif ($coreClr) { [IO.Path]::ChangeExtension($targetPath, '.dll') } else { $targetPath }
$planPath = if ($Plan) { (Resolve-Path -LiteralPath $Plan).ProviderPath } else { $null }
$runtimePath = (Resolve-Path -LiteralPath $RuntimeDirectory).ProviderPath
$engine = Join-Path $runtimePath 'MicrosoftInstrumentationEngine_x64.dll'
$config = Join-Path $runtimePath 'Rewrite.xml'
$extension = Join-Path $runtimePath 'Rewrite.dll'
foreach ($file in @($binary, $engine, $config, $extension)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required runtime or target file not found: $file" }
}
$before = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
if ($planPath) {
    $definition = Get-Content -Raw -LiteralPath $planPath | ConvertFrom-Json -AsHashtable
    if ($definition.version -notin @(1, 2)) { throw 'This Windows launcher accepts version 1/2 plans.' }
    $rules = if ($definition.version -eq 2) { @($definition.rules) } else { @($definition) }
    if (-not $rules.Count) { throw 'The plan contains no rules.' }
    foreach ($rule in $rules) {
        if ($before -ine $rule.sha256) { throw 'The target binary does not match the plan SHA-256.' }
    }
}
[xml]$xml = Get-Content -Raw -LiteralPath $config
$method = $xml.InstrumentationEngineConfiguration.InstrumentationMethod
if ($method.Module -cne 'Rewrite.dll' -or $method.ClassGuid -ine '{9F919AFE-C6F4-4263-A112-8DEC9947BC1E}') {
    throw 'The runtime configuration does not identify the packaged extension.'
}
$start = [Diagnostics.ProcessStartInfo]::new($targetPath)
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($key in @($start.Environment.Keys)) {
    if ($key -match '^(COR_(ENABLE_PROFILING|PROFILER)|CORECLR_(ENABLE_PROFILING|PROFILER)|MicrosoftInstrumentationEngine_|DOTNET_FAULT_INJECTION_)') {
        $null = $start.Environment.Remove($key)
    }
}
$prefix = if ($coreClr) { 'CORECLR' } else { 'COR' }
$start.Environment[$prefix + '_ENABLE_PROFILING'] = '1'
$start.Environment[$prefix + '_PROFILER'] = '{324F817A-7420-4E6D-B3C1-143FBED6D855}'
$start.Environment[$prefix + '_PROFILER_PATH_64'] = $engine
$start.Environment['MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent'] = $config
$start.Environment['MicrosoftInstrumentationEngine_DisableCodeSignatureValidation'] = '1'
if ($planPath) { $start.Environment['DOTNET_FAULT_INJECTION_PLAN'] = $planPath }
if ($PlanTarget) { $start.Environment['DOTNET_FAULT_INJECTION_SAMPLE_EXE'] = $binary }
foreach ($argument in $TargetArguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'The target exceeded the configured timeout.'
    }
    $output = $stdout.GetAwaiter().GetResult()
    $diagnostics = $stderr.GetAwaiter().GetResult()
    $lines = $diagnostics -split '\r?\n'
    $after = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
    [pscustomobject]@{
        Target = $targetPath
        Plan = $planPath
        ExitCode = $process.ExitCode
        PlanAccepted = [bool]($lines -cmatch '^plan accepted:')
        PlanRejected = [bool]($lines -cmatch '^plan rejected:')
        IlRewritten = [bool]($lines -cmatch '^IL rewritten:')
        Firings = @($lines -cmatch '^fault fired:').Count
        BinaryUnchanged = $before -ceq $after
        Output = $output.TrimEnd()
        Diagnostics = $diagnostics.TrimEnd()
    }
}
finally { $process.Dispose() }
