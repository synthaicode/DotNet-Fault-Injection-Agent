#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root ('work/rename-validation/windows-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$results = @()
function Assert-Result($Result, [int] $Firings, [string] $Expected) {
    if ($Result.ExitCode -ne 0 -or -not $Result.BinaryUnchanged -or
        ($Firings -gt 0 -and -not $Result.IlRewritten) -or $Result.Firings -ne $Firings -or
        ($Firings -gt 0 -and -not $Result.PlanAccepted) -or $Result.Output -notmatch $Expected) {
        throw ('Unexpected result: ' + ($Result | ConvertTo-Json -Depth 10))
    }
}
foreach ($name in @('ProfilerReturnValue','CoreFileTarget','FileWriteFailure','TcpConnectionRefused','SqlTimeout','OrderTimeout','OrderDelay')) {
    $directory = Join-Path $root "samples/$name"
    $parameters = @{ RuntimeDirectory = "$directory/tools/runtime" }
    $expected = ''
    $firings = 1
    if ($name -eq 'ProfilerReturnValue') {
        $parameters.Target = "$directory/target/ProfilerTarget.exe"
        $expected = '-1'; $firings = 0
        $baseline = & $parameters.Target
        if ($LASTEXITCODE -ne 0 -or ($baseline -join [Environment]::NewLine) -notmatch 'GetValue: 42') { throw 'Baseline return value failed' }
    } else {
        $plan = Get-Content -Raw "$directory/plan.json" | ConvertFrom-Json
        if ($name -in @('CoreFileTarget','FileWriteFailure')) {
            $plan.path = Join-Path $evidence "$name.txt"
        }
        $parameters.Plan = Join-Path $evidence "$name-plan.json"
        $plan | ConvertTo-Json -Depth 20 | Set-Content $parameters.Plan -Encoding utf8
        switch ($name) {
            CoreFileTarget { $parameters.Target = "$directory/target/CoreFileTarget.exe"; $parameters.TargetArguments = @($plan.path); $expected = 'IOException' }
            FileWriteFailure { $parameters.Target = "$directory/target/SqlTarget.exe"; $parameters.TargetArguments = @('--file',$plan.path); $expected = 'IOException' }
            TcpConnectionRefused { $parameters.Target = "$directory/target/SqlTarget.exe"; $parameters.TargetArguments = @('--network', [string]$plan.port); $expected = 'ConnectionRefused' }
            SqlTimeout { $parameters.Target = "$directory/target/SqlTarget.exe"; $expected = 'DB rows: 3' }
            default {
                $parameters.Target = "$directory/target/OrderAgentProbe.exe"
                $parameters.PlanTarget = "$directory/target/MultithreadedOrderInsert.exe"
                $mode = if ($name -eq 'OrderTimeout') { 'inject' } else { 'overlap' }
                $parameters.TargetArguments = @($parameters.PlanTarget, $plan.database, $mode)
                $expected = if ($mode -eq 'inject') { 'Injected TimeoutException recorded: True' } else { 'DB rows at completion: 1 -> 2' }
            }
        }
    }
    $result = & "$root/tools/Invoke-FaultInjection.ps1" @parameters
    $result | ConvertTo-Json -Depth 10 | Set-Content "$evidence/$name.json" -Encoding utf8
    Assert-Result $result $firings $expected
    if ($name -in @('CoreFileTarget','FileWriteFailure') -and -not (Test-Path -LiteralPath $plan.path)) { throw 'Second file write did not succeed' }
    if ($name -eq 'ProfilerReturnValue') {
        $after = & $parameters.Target
        if ($LASTEXITCODE -ne 0 -or ($after -join [Environment]::NewLine) -notmatch 'GetValue: 42') { throw 'Baseline after injection failed' }
    }
    $results += [pscustomobject]@{ Case = $name; Passed = $true; Firings = $result.Firings; BinaryUnchanged = $result.BinaryUnchanged }
    Write-Output "Passed: $name"
}
$results | ConvertTo-Json -Depth 10 | Set-Content "$evidence/summary.json" -Encoding utf8
Write-Output "Evidence: $evidence"
