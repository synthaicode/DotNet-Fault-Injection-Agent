# Plan-driven CoreCLR call injection

Version 3 JSON selects the target DLL, call site, conditions and fault. The planner binds matching source, Portable PDB and binary. The native extension inserts a shared managed BeforeCall hook immediately before the selected call, after the original arguments have been evaluated. The runtime applies the conditions and throws or delays.

## Supported scope

- External MemberRef call/callvirt in .NET 8, uniquely selected by source line and optional member name.
- One target module and call site per process.
- Exact environment equality and a starting matching invocation number.
- Synchronous System.InvalidOperationException, System.TimeoutException or System.IO.IOException with planned message, or a 1–10000 ms synchronous pre-call delay.
- Atomic Interlocked firing budget from 0 to 100.
- Tested on Linux x64 CoreCLR. Generic Windows native execution, .NET Framework v3, ReJIT, argument/business-object conditions and faulted Task injection are outside this mode.

Use v3 capabilities for the supported contract. Keep binary/PDB/source/site checks when changing conditions or faults. Legacy version 1/2 entry points remain separate.

## Build

Build src/Tool and src/Runtime in Release. The planner is exposed through the Tool's v3 entry point. Native requirements: g++, OpenSSL development headers, nlohmann/json 3.11.3, CLR Instrumentation Engine source and generated headers. Tested Engine revision: 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab.

ENGINE_HEADERS contains InstrumentationEngine.h; JSON_HEADERS contains nlohmann/json.hpp. Run src/Native/Linux/build.sh with Engine source, Engine headers, JSON headers and output directory. Place libInstrumentationEngine.so and the built DotNetFaultInjectionAgent.Runtime.deps.json in that output. Verification used Microsoft.CodeCoverage 18.7.0 Linux x64 Engine. Build the test image dotnet-fault-injection-runtime:local from src/Native/Linux/Dockerfile.

## Create and verify a plan

Use the deployed DLL, matching Portable PDB and source whose contents match the PDB checksum. A line:member-name selector disambiguates multiple external calls on a line. list reports method, IL offset, callee token and name.

After generation, edit ruleId, target.path for the execution deployment, conditions, fault and maxFirings. Keep generated hash, MVID and site fields. Run verify again. For another OS, target.path must be absolute for that OS; verify checks the binary supplied as its argument, while runtime validates the deployed path.

The full format is schemas/injection-plan.v3.schema.json. Final validation is performed by the planner and runtime parsers.

| Limit | Maximum |
| --- | --- |
| Plan size | 64 KiB |
| Nesting depth | 16 |
| Environment value | 256 UTF-8 bytes |
| Exception message | 1024 UTF-8 bytes |
| Native target DLL validation size | 16 MiB |

## Execution

Place DotNetFaultInjectionAgent.Runtime.dll beside the target application. Set the child process's DOTNET_ADDITIONAL_DEPS to the built runtime deps.json. The target needs neither a ProjectReference to the runtime nor recompilation for this deployment method.

Use trusted read-only deployments for target, Engine, extension, runtime and plan. Scope signature-validation bypass to the dedicated verification process. Each process fixes its plan snapshot; changes require a new process. The native extension embeds the parsed plan hash; the managed runtime rejects a different initial plan as a configuration error.

Observe plan accepted, IL rewritten and JSON event chaos.fired separately. A rejected plan leaves original calls active. Application success alone is not evidence of successful injection; also check application and external-system outcomes.

## Independent verification

Probe calls File.WriteAllText without a runtime reference. The verifier deploys the runtime and additional-deps. Place native extension and Engine in src/Native/Linux/bin first.

The script tests condition mismatch, zero budget, one firing across 12 concurrent calls, starting invocation and budget two, planned exception types/messages and delay. It also checks another call site, hash/MVID/member mismatch, unsupported faults, unknown/missing/duplicate keys, changed source and mutation after initialization. Results are stored under tests/PlanDrivenInjection/artifacts/verify-<id>. An optional old-runtime DLL argument adds coexistence verification.

## Commands and examples

```powershell
dotnet build src/Tool -c Release
dotnet build src/Runtime -c Release
dotnet run --project src/Tool -c Release -- v3 capabilities
```

```bash
bash src/Native/Linux/build.sh \
  "$ENGINE_SOURCE" "$ENGINE_HEADERS" "$JSON_HEADERS" /absolute/profiler
```

```powershell
dotnet run --project src/Tool -c Release -- v3 list target.dll Program.cs 22
dotnet run --project src/Tool -c Release -- v3 plan target.dll Program.cs 22 plan.json
dotnet run --project src/Tool -c Release -- v3 verify target.dll Program.cs 22 plan.json
```

```json
{
  "conditions": {
    "environment": { "CHAOS_TEST_CASE": "enabled" },
    "startAtInvocation": 3
  },
  "fault": {
    "kind": "throw",
    "exceptionType": "System.IO.IOException",
    "message": "planned file failure",
    "delayMs": 0
  },
  "maxFirings": 1
}
```

```bash
CORECLR_ENABLE_PROFILING=1 \
CORECLR_PROFILER='{324F817A-7420-4E6D-B3C1-143FBED6D855}' \
CORECLR_PROFILER_PATH=/absolute/profiler/libInstrumentationEngine.so \
MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent=/absolute/profiler/Rewrite.xml \
MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1 \
DOTNET_ADDITIONAL_DEPS=/absolute/profiler/DotNetFaultInjectionAgent.Runtime.deps.json \
DOTNET_FAULT_INJECTION_PLAN=/absolute/plan.json CHAOS_TEST_CASE=enabled \
dotnet /absolute/app/target.dll
```

```powershell
python tests/PlanDrivenInjection/verify.py dotnet-fault-injection-runtime:local
```
