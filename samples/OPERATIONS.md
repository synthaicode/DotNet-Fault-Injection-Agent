# Prepare and run samples

The sample READMEs describe plan meaning and expected behavior. This page describes deployment and execution.

## Create a plan with your AI assistant

Describe the application and test objective to the AI assistant in your environment. It inspects the target and uses the planning CLI to bind the call site, select supported conditions/faults and verify one plan.json. Review what the plan will do before execution. See the [copyable request](../src/Tool/AI-plan-guide.md).

## Prepare deployments

Preparation builds the repository's test fixtures and generates plans from their deployed binaries. It does not run the sample applications and does not mean a user's existing application must be rebuilt for injection.

Requirements: .NET 10 SDK, .NET 8 targeting support, .NET Framework 4.8 reference assemblies, the planning tool and built Windows/Linux runtimes.

```powershell
dotnet pack src/Tool -c Release -o artifacts/packages
dotnet tool install DotNetFaultInjectionAgent.Tool --version 0.1.0-preview.4 --tool-path .tools --add-source artifacts/packages
.tools/dotnet-fault-injection.exe v3 capabilities
./tools/Prepare-Samples.ps1
```

These commands use a locally built package; they do not assume NuGet.org publication. Use `-ToolDirectory <directory>` for a different tool installation. Native build instructions: [Windows](../src/Native/Windows/DEVELOPMENT.md), [Linux](../tests/PlanDrivenInjection/DEVELOPMENT.md).

Use `-Force` only when intentionally replacing generated plans/deployments. For order samples, provide both `-OrderProject` and `-OrderBinary`, with the matching Portable PDB beside the external EXE. Without them the order deployments are skipped.

## Windows execution

Use PowerShell 7. In a sample folder, target/ holds the application, reference/ holds planning inputs, plan.json is the execution plan and tools/runtime holds engine, extension and XML. The launcher sets profiler environment variables.

```powershell
$plan = Get-Content -Raw ./plan.json | ConvertFrom-Json
./tools/Invoke-FaultInjection.ps1 -Target ./target/CoreFileTarget.exe -Plan ./plan.json -RuntimeDirectory ./tools/runtime -TargetArguments $plan.path
```

| Sample | Target | TargetArguments |
| --- | --- | --- |
| CoreFileTarget | CoreFileTarget.exe | plan.path |
| SqlTimeout | SqlTarget.exe | none |
| FileWriteFailure | SqlTarget.exe | --file, plan.path |
| TcpConnectionRefused | SqlTarget.exe | --network, plan.port |
| ProfilerReturnValue | ProfilerTarget.exe | none; omit Plan |

TargetArguments are application arguments, not plan fields. File tests require a nonexistent destination. Reusing the same plan after a successful write can be rejected because the file already exists.

SharedFlags uses `target/SyncService.exe --console` without the injector. See [service registration](../tests/Fixtures/SyncService/DEVELOPMENT.md).

## Order samples

The verified binary and launched EXE differ. Set Target to target/OrderAgentProbe.exe and PlanTarget to target/MultithreadedOrderInsert.exe, with Plan=plan.json and RuntimeDirectory=tools/runtime.

TargetArguments are the external sample EXE's absolute path, plan.database and a mode: inject for OrderTimeout or overlap for OrderDelay. The host creates a uniquely named test database and deletes it afterward. Use an isolated test environment, not an existing application database.

## Linux execution

The generic sample uses tools/planner/Planner.dll and tools/Invoke-LinuxSample.ps1, not the Windows version 1/2 launcher. Have Docker image dotnet-fault-injection-runtime:local available.

```powershell
./tools/Invoke-LinuxSample.ps1 -Sample .
./tools/Invoke-LinuxSample.ps1 -Sample . -ConditionMismatch
```

The plan and logs used for each run are saved under evidence/.

## Observe results

Check plan acceptance, rejection, IL rewriting, firing count and binary immutability separately. Exit code zero alone does not prove injection succeeded. For Linux, check plan accepted, IL rewritten, chaos.fired and task results. Compare the observed application/file/database state with the sample's expectations.
