# Original design overview

This is the original design context, not a claim that every planned feature is implemented. [Generic CoreCLR injection](../../tests/PlanDrivenInjection/README.md) later added verified version 3 plans for Linux x64/.NET 8.

## Objective and boundaries

Evaluate exception handling and recovery in .NET Framework Windows services without rebuilding business source or modifying deployed EXE/DLL files. In-process IL rewriting was selected for investigation. Restrict intervention to the chosen process; assess indirect effects through shared locks or external systems per test. Users describe business processing, conditions and faults; C++, IL and tokens remain tool internals.

## Intended workflow

1. Resolve sources/references with Roslyn and extract caller, API, source location and nearby exception handling. The AI explains its selection and values static analysis cannot establish.
2. Confirm site, conditions and fault. For example, fail only the first pre-send call to a specific test DB procedure. Stop if converging business paths cannot be distinguished by supported conditions.
3. Match rules against supported APIs/faults. Validate PE/Portable PDB identity, source checksums, sequence points and IL; generate a plan only for a unique instruction. Bind hash, MVID, token, offset and expected callee.
4. The proposed external agent identifies EXE path, PID and creation time and manages enable/disable. In-process code checks loaded module/instruction and command kind/server/DB/procedure; disabled or mismatching calls pass through.
5. Record acceptance, rewriting, JIT results, enablement and firing separately; observe application recovery separately. Disabling subsequent firing is distinct from restoring IL.

A TimeoutException proves the intervention path but does not test a branch that catches a particular SqlException number. Unsupported construction modes must be rejected with a reason. Pre-send failure and a lost response after database processing are distinct cases.

## Implementation options

[CLR Instrumentation Engine](https://github.com/microsoft/CLRInstrumentationEngine/blob/main/DESIGN-NOTES.md) provides a profiler host and IL graph; native extension, selection, bridge, rule matching and agent integration still require implementation. It was the first PoC candidate. A custom profiler would require COM entry points, callbacks, IL branch/exception-region handling and architecture-specific distribution; reconsider if the engine cannot support required loading/rewrites. See [CLR profiling setup](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/setting-up-a-profiling-environment).

## Original minimum acceptance criteria

Use a fixed .NET Framework fixture and isolated DB. Intervene at initial JIT at one synchronous SqlCommand.ExecuteNonQuery call; consider ReJIT only if adding sites after startup becomes necessary.

- Select a source candidate and reject mismatched deployment/PDB or ambiguous mapping before application.
- Fire TimeoutException before send only at the matching selected site; concurrent matches consume at most one enabled budget slot.
- Other sites/commands/processes, disabled calls and calls after budget exhaustion behave normally.
- Confirm no DB write for the failed command, observe exception handling, and preserve deployed binary hashes.
- Reject unsupported SqlException/async requests; distinguish synchronous throws from faulted Tasks in any future support.
- After the fixture passes, verify profiler load and process restriction in a dedicated service.

## Unknowns at the time of the original design

Profiler loading, rewriting, firing and service operation had not yet been verified. Environment checks included Windows/Framework version, bitness, SqlClient, existing APM, service startup, and matching source/PDB. NGen/inlining effects, rule format and agent communication were unresolved. Later runtime evidence must be read separately from these original unknowns.
