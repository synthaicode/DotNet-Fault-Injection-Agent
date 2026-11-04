#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Source,
    [Parameter(Mandatory)][string] $Executable,
    [Parameter(Mandatory)][string] $References,
    [Parameter(Mandatory)][int] $ConfirmedCandidate,
    [Parameter(Mandatory)][string] $Server,
    [Parameter(Mandatory)][string] $Database,
    [Parameter(Mandatory)][string] $Procedure,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int] $MaxFirings,
    [Parameter(Mandatory)][string] $OutputPlan
)

$ErrorActionPreference = 'Stop'
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPlan)
$temp = Join-Path (Split-Path -Parent $outputPath) ('.plan-' + [guid]::NewGuid() + '.json')
try {
    dotnet run --project "$PSScriptRoot/SourceMap.csproj" -- $Source $Executable $References $ConfirmedCandidate $temp
    if ($LASTEXITCODE -ne 0) { throw 'SourceMap could not generate the confirmed plan.' }

    $plan = Get-Content -Raw -LiteralPath $temp | ConvertFrom-Json -AsHashtable
    $plan.server = $Server
    $plan.database = $Database
    $plan.procedure = $Procedure
    $plan.maxFirings = $MaxFirings
    $plan | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8NoBOM -LiteralPath $temp

    dotnet run --no-build --project "$PSScriptRoot/SourceMap.csproj" -- $Source $Executable $References verify $temp
    if ($LASTEXITCODE -ne 0) { throw 'The confirmed plan failed verification.' }

    [IO.File]::Move($temp, $outputPath)
    [pscustomobject]@{
        Plan = $outputPath
        Verified = $true
        RuntimeScope = 'The PoC runtime currently accepts only the dedicated SqlTarget.exe fixture.'
    }
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}
