# Runtime verification

| Directory | Purpose |
| --- | --- |
| Fixtures | CoreFileTarget, SqlTarget, ProfilerTarget and SyncService targets |
| CoreFileTarget | Dedicated CoreCLR file-injection instructions |
| OrderAgentProbe | External order-sample host and Bridge |
| PlanDrivenInjection | Independent Probe and Linux injection tests |

Test projects are not NuGet publication targets. Use [Prepare-Samples.ps1](../tools/Prepare-Samples.ps1), [Windows sample tests](../tools/Test-RenamedWindowsSamples.ps1) and [service tests](../tools/Test-RenamedService.ps1).

Build and place the [native extension and engine](PlanDrivenInjection/DEVELOPMENT.md) under src/Native/Linux/bin, then run:

```powershell
python tests/PlanDrivenInjection/verify.py dotnet-fault-injection-runtime:local
```

[Package verification](../tools/Test-Package.ps1) checks content, clean installation and plan generation/verification.
