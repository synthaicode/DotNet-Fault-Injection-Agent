# Prepare and run samples

## Download and use the bundled Windows runtime

After this package is published to NuGet.org, install the .NET tool and export the execution files:

```powershell
dotnet tool install --global DotNetFaultInjectionAgent.Tool --prerelease
dotnet-fault-injection guide
dotnet-fault-injection runtime list
dotnet-fault-injection runtime export --output ./fault-runtime
```

Before publication, use the same installation command with `--add-source <local-package-directory>` and `--version 0.1.0-preview.4` instead of `--prerelease`. The download/export workflow is validated against a locally built NuGet package; this documentation does not imply NuGet.org publication has occurred.

`fault-runtime` must be a new directory. Export verifies the payload checksums and does not replace an existing directory. It contains the Windows x64 Engine, Rewrite.dll, Rewrite.xml, Invoke-FaultInjection.ps1, provenance/checksum manifest and dependency license notices. No native compilation or repository checkout is required on the user's machine.

Windows prerequisites are .NET 10 for the CLI, PowerShell 7 for execution, and the runtime required by the target application. The included native binaries have system DLL dependencies only in the inspected build. Keep the exported files together.

Create and verify a plan for the actual supported Windows target, then launch from PowerShell 7:

```powershell
./fault-runtime/Invoke-FaultInjection.ps1 -Target C:/test/SqlTarget.exe -Plan C:/test/plan.json
```

For the .NET CoreFileTarget fixture, provide its apphost EXE to Target and bind the plan to its matching managed DLL. Pass the planned destination path through `-TargetArguments`. The launcher selects COR_* for Framework or CORECLR_* for the dedicated CoreCLR fixture and supplies the bundled Engine/config/extension. Check PlanAccepted, IlRewritten, Firings, BinaryUnchanged and the application result.

The Windows payload supports the existing dedicated Framework/CoreCLR fixtures and order host; bundling does not add support for arbitrary application binaries. It uses an unsigned extension with signature-validation bypass limited to the test child process. Services require their own service configuration; exporting does not register a service.

Both win-x64 and linux-x64 execution payloads are bundled. Their launchers and platform requirements differ; see the Linux section below.

The sample READMEs describe plan meaning and expected behavior. This page describes deployment and execution.

## Create a plan with your AI assistant

Describe the application and test objective to the AI assistant in your environment. It inspects the target and uses the planning CLI to bind the call site, select supported conditions/faults and verify one plan.json. Review what the plan will do before execution. See the [copyable request](../src/Tool/AI-plan-guide.md).

## Maintainers: build repository sample deployments

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


## Choose by target runtime and operating system

The CLI itself requires .NET 10. That requirement is separate from the runtime of the application being tested.

Run `dotnet-fault-injection capabilities` to see the supported target combinations. Choose the target runtime and OS, not a JSON-format generation.

| Application | Support and planning command | Startup and deployment |
| --- | --- | --- |
| .NET Framework 4.8 / Windows x64 | Dedicated fixtures and order host: `windows-fixture list/plan/verify` | Launch the EXE with COR_* profiler settings and the Windows engine/extension/XML. Some fixtures contain built-in fault helpers. |
| .NET 10 / Windows x64 | Dedicated CoreFileTarget only: `windows-fixture list/plan/verify` | Launch the apphost EXE with CORECLR_* settings; bind the plan to its managed DLL, not the apphost. This does not establish generic Windows .NET support. |
| .NET 8 / Linux x64 | Generic external-call injection: `dotnet list/plan/verify` | Launch `dotnet target.dll` with CORECLR_* settings, Linux engine/extension/XML, the managed injection Runtime beside the application, and DOTNET_ADDITIONAL_DEPS pointing to its deps.json. |

On Windows, the sample launcher detects the adjacent runtimeconfig.json to select COR_* or CORECLR_* settings. The Windows launcher accepts dedicated fixture plans; it cannot execute the generic Linux plan simply by changing a prefix. Windows services require service-specific startup configuration rather than a normal process launch.

For generic .NET/Linux planning:

```text
dotnet-fault-injection capabilities
dotnet-fault-injection dotnet list target.dll Program.cs 22
dotnet-fault-injection dotnet plan target.dll Program.cs 22 plan.json
dotnet-fault-injection dotnet verify target.dll Program.cs 22 plan.json
```

For dedicated Windows fixtures, supply source/project, the actual managed binary and the matching reference-assembly directory:

```text
dotnet-fault-injection windows-fixture list <source|project> <binary> <references>
dotnet-fault-injection windows-fixture plan <source|project> <binary> <references> <selection> <plan.json>
dotnet-fault-injection windows-fixture verify <source|project> <binary> <references> <plan.json>
```

Plan-format numbers are internal compatibility details. Existing `v3` and unprefixed fixture commands remain available for existing scripts. Neither route makes unsupported runtime/OS combinations supported.


## Use the bundled Linux x64 runtime

The same NuGet tool package includes both win-x64 and linux-x64 execution files. `runtime export --output <new-directory>` selects the current x64 OS; use `--rid linux-x64` or `--rid win-x64` to export explicitly, including from another OS. Existing directories are rejected and each payload's checksums are verified.

On Linux, after installing the test package from the configured GitHub Packages feed:

```bash
dotnet-fault-injection runtime list
dotnet-fault-injection runtime export --rid linux-x64 --output ./fault-runtime
bash ./fault-runtime/Invoke-FaultInjection.sh --target /absolute/app/target.dll --plan /absolute/plan.json -- application-arguments
```

Create and verify the plan with `dotnet-fault-injection dotnet plan` and `dotnet verify` against the actual target DLL, matching source and Portable PDB before running. The target.path in the plan must be the target's absolute path in the execution environment.

The export contains libInstrumentationEngine.so, libDotNetFaultInjectionAgent.so, Rewrite.xml, DotNetFaultInjectionAgent.Runtime.dll/deps.json, the Bash launcher, dependency reports, a manifest and license notices. Users do not build these components.

Requirements: Linux x64 with glibc (not Alpine/musl), OpenSSL 3/libcrypto.so.3, libstdc++, the target .NET 8 runtime, and .NET 10 for the CLI. The native payload is built in Debian 12; execution is verified in a Debian 12 .NET 8 container and on the Ubuntu 24.04 Actions runner. This does not establish compatibility with every Linux distribution, older glibc, ARM64 or macOS.

The launcher configures CORECLR_* and DOTNET_ADDITIONAL_DEPS for the target child. It places the packaged managed injection DLL beside the target if absent, requiring write access to that directory; it keeps this DLL for subsequent runs. It accepts an identical existing DLL and refuses a different one rather than replacing it. The target DLL itself is not rebuilt or modified. Use a new process for a changed plan. Match acceptance, rewriting, chaos.fired and application/external-state observations separately.
