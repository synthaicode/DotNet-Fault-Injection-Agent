# Internal names and compatibility

The display name is DotNet Fault Injection Agent and the repository is DotNet-Fault-Injection-Agent. Internal identifiers migrated as follows.

| Previous name | Current name |
| --- | --- |
| `WindowsChaosAgent` | `DotNetFaultInjectionAgent` |
| `WindowsChaosAgent.Runtime.dll` / `.deps.json` | `DotNetFaultInjectionAgent.Runtime.dll` / `.deps.json` |
| `WindowsChaosAgent.Tool` / `dotnet-chaos` | `DotNetFaultInjectionAgent.Tool` / `dotnet-fault-injection` |
| `Invoke-Chaos.ps1` | `tools/Invoke-FaultInjection.ps1` |
| `WindowsChaosAgentProbe` | `DotNetFaultInjectionAgentProbe` |
| `WindowsChaosSyncProbe` | `DotNetFaultInjectionAgentSyncProbe` |
| `ChaosOrderBridge` | `DotNetFaultInjectionAgent.OrderBridge` |
| `ChaosOrderAgent_` | `DotNetFaultInjectionAgentOrder_` |
| `WINDOWS_CHAOS_*` | `DOTNET_FAULT_INJECTION_*` |
| `MicrosoftInstrumentationEngine_ConfigPath64_Chaos` | `MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent` |
| `libChaosRewrite.so` | `libDotNetFaultInjectionAgent.so` |
| `windows-chaos-runtime:local` | `dotnet-fault-injection-runtime:local` |

The product name in the exception message, its OrderAgentProbe comparison, and the Windows extension XML Name were changed together. Rewrite.dll, Rewrite.xml, profiler GUID, JSON plan versions 1/2/3 and event chaos.fired remain stable.

## Migration

No aliases are provided for old CLI names, environment variables or DLL names. Deploy a consistent version rather than renaming only part of an old deployment.

1. Build the new CLI, Windows/Linux extensions and runtime in a separate empty directory. Install the local package using the [preparation steps](../../samples/OPERATIONS.md). NuGet.org publication is separate.
2. Deploy XML, native extension, runtime DLL/deps.json and launcher from the same version. The target does not need a ProjectReference to the new runtime.
3. Update launch variables. Rebuilding fixtures changes hash/MVID/PDB: regenerate and verify plans. For existing applications, verify against the actual binary to be executed.
4. Before stopping/unregistering an old service, check its owner, executable and suitability for stopping. Register the new service and explicitly configure its Environment values. Existing services are not automatically changed.
5. Confirm acceptance, rewriting, firing count and target outcomes before switching. Roll back using the saved complete old deployment and old startup settings.

These steps concern dedicated test services. In-place migration of existing business services and signed-extension distribution were not verified.

## Reproduce verification

- `python tests/PlanDrivenInjection/verify.py` runs 17 Linux x64/.NET 8 cases. Supply image name as the first positional argument and an actual old WindowsChaosAgent.Runtime.dll as the second to add coexistence verification.
- `tools/Test-RenamedWindowsSamples.ps1` runs seven prepared samples and checks firing, target results and binary immutability. Preparing order tests requires the external sample project.
- `tools/Test-RenamedService.ps1 -Bundle <directory>` registers dedicated services as administrator, checks baseline/one firing/shared flags and removes registration. Bundle SqlTarget.exe, SyncService.exe, a verified plan.json, Rewrite.dll, Rewrite.xml and MicrosoftInstrumentationEngine_x64.dll.

See the [recorded rename verification](../../work/sessions/2026-11-07_internal_name_verification.md).
