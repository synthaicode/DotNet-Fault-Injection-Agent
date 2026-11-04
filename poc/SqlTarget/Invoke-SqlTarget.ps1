#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Target,
    [Parameter(Mandatory)][string] $Plan,
    [Parameter(Mandatory)][string] $Engine,
    [Parameter(Mandatory)][string] $ExtensionConfig,
    [switch] $Concurrent
)

$ErrorActionPreference = 'Stop'
$targetFile = Get-Item -LiteralPath $Target
if ($targetFile.PSIsContainer -or $targetFile.Name -ne 'SqlTarget.exe') {
    throw 'This launcher accepts only the SqlTarget.exe test fixture.'
}
$planPath = (Resolve-Path -LiteralPath $Plan).ProviderPath
$enginePath = (Resolve-Path -LiteralPath $Engine).ProviderPath
$configPath = (Resolve-Path -LiteralPath $ExtensionConfig).ProviderPath

$start = [System.Diagnostics.ProcessStartInfo]::new($targetFile.FullName)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($key in @($start.Environment.Keys)) {
    if ($key -match '^(COR_(ENABLE_PROFILING|PROFILER)|MicrosoftInstrumentationEngine_|WINDOWS_CHAOS_)') {
        $null = $start.Environment.Remove($key)
    }
}
$start.Environment['COR_ENABLE_PROFILING'] = '1'
$start.Environment['COR_PROFILER'] = '{324F817A-7420-4E6D-B3C1-143FBED6D855}'
$start.Environment['COR_PROFILER_PATH'] = $enginePath
$start.Environment['COR_PROFILER_PATH_64'] = $enginePath
$start.Environment['MicrosoftInstrumentationEngine_ConfigPath64_Chaos'] = $configPath
$start.Environment['MicrosoftInstrumentationEngine_DisableCodeSignatureValidation'] = '1'
$start.Environment['WINDOWS_CHAOS_PLAN'] = $planPath
if ($Concurrent) {
    $start.ArgumentList.Add('--concurrent')
}

$process = [System.Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'The test process exceeded 30 seconds.'
    }

    $output = $stdout.GetAwaiter().GetResult()
    $diagnostics = $stderr.GetAwaiter().GetResult()
    $lines = $diagnostics -split '\r?\n'
    [pscustomobject]@{
        Target = $targetFile.FullName
        Plan = $planPath
        ProcessId = $process.Id
        ExitCode = $process.ExitCode
        PlanAccepted = [bool]($lines -cmatch '^plan accepted:')
        PlanRejected = [bool]($lines -cmatch '^plan rejected:')
        IlRewritten = [bool]($lines -cmatch '^IL rewritten:')
        Firings = @($lines -cmatch '^fault fired:').Count
        Output = $output.TrimEnd()
        Diagnostics = $diagnostics.TrimEnd()
    }
}
finally {
    $process.Dispose()
}
