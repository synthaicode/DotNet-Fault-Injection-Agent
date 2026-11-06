#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $Bundle,
    [string] $ResultUri
)
$ErrorActionPreference = 'Stop'
if (-not $Bundle) { $Bundle = $PSScriptRoot }
$serviceName = 'DotNetFaultInjectionAgentProbe'
$bundlePath = (Resolve-Path -LiteralPath $Bundle).ProviderPath
$evidence = Join-Path $bundlePath ('evidence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'Test service already exists; refusing to change it.' }
$exe = Join-Path $bundlePath 'SqlTarget.exe'
$planPath = Join-Path $bundlePath 'plan.json'
$definition = Get-Content -Raw -LiteralPath $planPath | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if ($hash -ine $definition.sha256) { throw 'Target hash differs from the verified plan.' }
$created = $false
$result = [ordered]@{ Passed = $false; ServiceName = $serviceName; TargetHash = $hash; OS = [Environment]::OSVersion.VersionString }
try {
    New-Service -Name $serviceName -BinaryPathName ('"' + $exe + '" --service') -StartupType Manual | Out-Null
    $created = $true
    foreach ($mode in @('baseline','injected')) {
        $log = Join-Path $evidence "$mode.log"
        if ($mode -eq 'injected') {
            $environment = @(
                'COR_ENABLE_PROFILING=1',
                'COR_PROFILER={324F817A-7420-4E6D-B3C1-143FBED6D855}',
                "COR_PROFILER_PATH_64=$bundlePath\MicrosoftInstrumentationEngine_x64.dll",
                "COR_PROFILER_PATH=$bundlePath\MicrosoftInstrumentationEngine_x64.dll",
                "MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent=$bundlePath\Rewrite.xml",
                'MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1',
                "DOTNET_FAULT_INJECTION_PLAN=$planPath",
                'DOTNET_FAULT_INJECTION_SERVICE_PROBE=1',
                "DOTNET_FAULT_INJECTION_DIAGNOSTICS=$evidence\agent.log"
            )
            New-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null
        }
        & sc.exe start $serviceName $log | Out-File "$evidence/$mode-scm.log"
        if ($LASTEXITCODE -ne 0) { throw "Service start failed: $mode" }
        $service = Get-Service $serviceName
        $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $result["${mode}Running"] = $true
        Stop-Service $serviceName
        (Get-Service $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        $text = [IO.File]::ReadAllText($log)
        $result["${mode}Log"] = $text
        if ($mode -eq 'baseline') {
            if ([regex]::Matches($text, 'InvalidOperationException').Count -ne 2 -or $text -match 'TimeoutException') { throw 'Baseline did not reach the closed-connection API twice.' }
        } else {
            if ($text -notmatch 'attempt=1 TimeoutException' -or $text -notmatch 'attempt=2 InvalidOperationException' -or [regex]::Matches($text, 'fault fired:').Count -ne 1) { throw 'Expected exactly one injected timeout followed by the original API.' }
            $diagnostics = [IO.File]::ReadAllText("$evidence/agent.log")
            $result.AgentLog = $diagnostics
            if ($diagnostics -notmatch 'plan accepted:' -or $diagnostics -notmatch 'IL rewritten:') { throw 'Missing profiler acceptance or rewrite evidence.' }
        }
    }
    $result.BinaryUnchanged = $hash -eq (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if (-not $result.BinaryUnchanged) { throw 'Target changed during service test.' }
    $syncName = 'DotNetFaultInjectionAgentSyncProbe'
    if (Get-Service $syncName -ErrorAction SilentlyContinue) { throw 'Sync test service already exists.' }
    $syncExe = Join-Path $bundlePath 'SyncService.exe'
    $syncHash = (Get-FileHash $syncExe -Algorithm SHA256).Hash
    New-Service -Name $syncName -BinaryPathName ('"' + $syncExe + '"') -StartupType Manual | Out-Null
    try {
        $syncLog = Join-Path $evidence 'sync.log'
        & sc.exe start $syncName $syncLog | Out-File "$evidence/sync-scm.log"
        if ($LASTEXITCODE -ne 0) { throw 'Sync service start failed.' }
        $syncService = Get-Service $syncName
        $syncService.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $result.SyncRunning = $true
        $result.SyncLog = [IO.File]::ReadAllText($syncLog)
        if ([regex]::Matches($result.SyncLog, 'first read=False; recheck after publication=True').Count -ne 2) { throw 'Sync observations failed.' }
        $result.SyncBinaryUnchanged = $syncHash -eq (Get-FileHash $syncExe -Algorithm SHA256).Hash
        if (-not $result.SyncBinaryUnchanged) { throw 'Sync target changed.' }
    } finally {
        $syncService = Get-Service $syncName
        if ($syncService.Status -ne 'Stopped') { Stop-Service $syncName; $syncService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
        $syncService.Dispose()
        & sc.exe delete $syncName | Out-File "$evidence/delete-sync-scm.log"
        if ($LASTEXITCODE -ne 0) { throw 'Sync service deletion failed.' }
        $result.SyncRemoved = -not [bool](Get-Service $syncName -ErrorAction SilentlyContinue)
        if (-not $result.SyncRemoved) { throw 'Sync service still exists.' }
    }
    $result.Passed = $true
} catch {
    $result.Error = $_.ToString()
} finally {
    if ($created) {
        $service = Get-Service $serviceName
        if ($service.Status -ne 'Stopped') { Stop-Service $serviceName; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
        $service.Dispose()
        & sc.exe delete $serviceName | Out-File "$evidence/delete-scm.log"
        if ($LASTEXITCODE -ne 0) { $result.Passed = $false; $result.CleanupError = 'Service deletion failed' }
        $result.ServiceRemoved = -not [bool](Get-Service $serviceName -ErrorAction SilentlyContinue)
        if (-not $result.ServiceRemoved) { $result.Passed = $false }
    }
    $json = $result | ConvertTo-Json -Depth 10
    $json | Set-Content "$evidence/result.json" -Encoding UTF8
    if ($ResultUri) { Invoke-RestMethod -Method Post -TimeoutSec 20 -Uri $ResultUri -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null }
    $json
}
if (-not $result.Passed) { throw "Service test failed; see $evidence/result.json" }
