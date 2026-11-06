# Windows service and shared-flag verification

This .NET Framework 4.8 x64 fixture runs the same trial in console and service modes. The reader runs first, then the writer publishes true; after Join, the reader checks again. Both ordinary and volatile fields first read false and then true. Because Join synchronizes the trial, it does not determine visibility/optimization behavior of asynchronous polling. Volatile cannot update a control path that reads only once before publication.

## Service registration

Use an administrator PowerShell in a dedicated non-production environment. Stop if the chosen service name is already in use. Supply an absolute log path writable by the service account. Build and registration commands follow.

## Historical ChaosTest result

On 2026-11-04, the fixture was built in the Hyper-V guest and registered as a dedicated service. Log output and short-method syntax were adapted to the guest compiler without changing trial synchronization. SCM reported RUNNING and exit code zero; both fields showed false then true.

An initial status-wait error left the running service marked for deletion. A normal stop request did not complete, so the dedicated process was identified by PID and absolute EXE path and stopped; absence of registration was confirmed. Logs remained in the guest. Polling without Join remains unverified. Profiler service load/firing is documented in the [SQL service tests](../SqlTarget/DEVELOPMENT.md).

## Commands and examples

```powershell
dotnet build tests/Fixtures/SyncService -c Release
$exe = (Resolve-Path ./tests/Fixtures/SyncService/bin/Release/net48/SyncService.exe).Path
& $exe --console
```

```powershell
$name = 'DotNetFaultInjectionAgentSyncProbe'
$log = Join-Path $env:ProgramData ("DotNetFaultInjectionAgentSyncProbe-" + [guid]::NewGuid() + ".log")
if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw 'Service name is in use.' }
New-Service -Name $name -BinaryPathName ('"' + $exe + '"') -StartupType Manual
try {
    sc.exe start $name $log
    if ($LASTEXITCODE -ne 0) { throw 'Service start failed.' }
    (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
    Get-Content -LiteralPath $log
}
finally {
    Stop-Service -Name $name -ErrorAction SilentlyContinue
    sc.exe delete $name
}
```

```text
ordinary: first read=False; recheck after publication=True
volatile: first read=False; recheck after publication=True
```
