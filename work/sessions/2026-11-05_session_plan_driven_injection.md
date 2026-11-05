## 2026-11-05: Plan-driven CoreCLR injection

### Request
The user requested that target, conditions, and fault be selected by plan,
with no Ksql.Linq-specific profiler behavior, followed by Ksql.Linq retesting.

### Implementation
Added version 3 through the existing SourceMap entry point. The planner binds
a source/PDB-verified MemberRef call site to assembly SHA-256 and MVID.
The native extension inserts a managed BeforeCall hook. A shared strict plan
model drives environment equality, starting invocation, atomic firing budget,
three supported exception types/messages, and bounded synchronous delay.
The plan snapshot hash is embedded and checked by the managed runtime.
Deployment uses an additional runtime DLL and additional-deps, so the target
application needs no project dependency on the injection runtime.

### Verification
The final native binary SHA-256 is
BF43F5D9528291083E6761DE3621B236B3F1FFEB1BE2B0C6C415764ABAE8EDE2.
Seventeen independent file-target cases passed, including 12 concurrent calls,
budget/condition cases, plan-specified types/messages/delay, identity mismatch,
malformed plans and a plan changed after profiler initialization.
The legacy SourceMap list smoke test and v3 verification entry passed.
JSON schema, project XML and extension XML parsed.

Used the same profiler/runtime binaries with local Ksql.Linq 1.1.1 whose five
reviewed sources match public f9a3134 Portable PDB checksums. Six deployment
cases passed: baseline before/after, message-only exception control/positive,
and window control/delay. Positive recovery waited about 6.16s despite caller
cancellation; B succeeded and was the only new record in that interval.
The window delay was planned immediately before TryRemove, and a message
added during the delay was absent from later output. Empty-string and SQL
component behavior was also reconfirmed.

### Evidence
Independent cases are under
poc/PlanDrivenInjection/artifacts/verify-e96d0548ca774d51bb9aad0e6abafccd.
Ksql-specific harnesses, plans, raw logs and hashes are under
C:/dev/ksql-plan-driven-verification-20261105.
Those deployment artifacts and native dependencies are not source commits.

### Boundaries
Validated on Linux x64/.NET 8 in Docker on Windows. Windows native/service,
.NET Framework v3, argument/business-object matching, ReJIT, arbitrary
exception construction and faulted-Task injection are outside this mode.
The new feature targets the existing poc/profiler-startup branch.
No Ksql.Linq source change or tool package release was performed.
