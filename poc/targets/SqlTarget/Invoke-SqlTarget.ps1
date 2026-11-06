#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Target,
    [Parameter(Mandatory)][string] $Plan,
    [Parameter(Mandatory)][string] $Engine,
    [Parameter(Mandatory)][string] $ExtensionConfig,
    [switch] $Concurrent,
    [switch] $ThreeSites,
    [ValidateSet("enable", "disable", "call")][string[]] $ControlSequence
)

$ErrorActionPreference = 'Stop'
if (($Concurrent -and $ThreeSites) -or (($Concurrent -or $ThreeSites) -and $ControlSequence.Count)) {
    throw 'Concurrent, ThreeSites and ControlSequence are mutually exclusive.'
}
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
$start.RedirectStandardInput = [bool]$ControlSequence.Count
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

if ($ThreeSites) {
    $start.ArgumentList.Add('--three-sites')
}
if ($ControlSequence.Count) {
    $start.ArgumentList.Add('--controlled')
}

function Read-ControlReply($Process) {
    $reply = $Process.StandardOutput.ReadLineAsync()
    if (-not $reply.Wait(10000)) {
        throw 'The controlled process did not reply within 10 seconds.'
    }
    $line = $reply.GetAwaiter().GetResult()
    if ($null -eq $line) {
        throw 'The controlled process closed its output before replying.'
    }
    $line
}

$process = [System.Diagnostics.Process]::Start($start)
try {
    $stderr = $process.StandardError.ReadToEndAsync()
    $replies = @()
    if ($ControlSequence.Count) {
        $ready = Read-ControlReply $process
        if ($ready -cne 'ready: disabled') {
            throw "Unexpected control greeting: $ready"
        }
        $replies = @($ready; foreach ($command in $ControlSequence) {
            $process.StandardInput.WriteLine($command.ToLowerInvariant())
            $process.StandardInput.Flush()
            Read-ControlReply $process
        })
        $process.StandardInput.WriteLine('quit')
        $process.StandardInput.Close()
    }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'The test process exceeded 30 seconds.'
    }

    $output = $stdout.GetAwaiter().GetResult()
    $diagnostics = $stderr.GetAwaiter().GetResult()
    $lines = $diagnostics -split '\r?\n'
    $counts = [ordered]@{}
    foreach ($line in $lines) {
        if ($line -cmatch '^fault fired: methodToken=(\d+) ') {
            $token = $Matches[1]
            $counts[$token] = [int]$counts[$token] + 1
        }
    }
    [pscustomobject]@{
        Target = $targetFile.FullName
        Plan = $planPath
        ProcessId = $process.Id
        ExitCode = $process.ExitCode
        PlanAccepted = [bool]($lines -cmatch '^plan accepted:')
        PlanRejected = [bool]($lines -cmatch '^plan rejected:')
        IlRewritten = [bool]($lines -cmatch '^IL rewritten:')
        Firings = @($lines -cmatch '^fault fired:').Count
        FiringsByMethod = $counts
        ControlReplies = $replies
        Output = $output.TrimEnd()
        Diagnostics = $diagnostics.TrimEnd()
    }
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    $process.Dispose()
}
