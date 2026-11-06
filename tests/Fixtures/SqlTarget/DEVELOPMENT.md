# Dedicated synchronous SQL and I/O injection tests

Build tests/Fixtures/SqlTarget for .NET Framework 4.8 x64 Release. Use local MSSQLLocalDB; temporary tables and two temporary procedures exist only in the test connection and disappear when it closes. Run the same EXE without profiling, with the selected verified plan/extension, and without profiling again.

| Run | Selected matching call | Other site | Other procedure | Second matching call | DB rows |
| --- | --- | --- | --- | --- | ---: |
| Disabled | success | success | success | success | 4 |
| Extension | TimeoutException | success | success | success | 3 |
| Disabled again | success | success | success | success | 4 |

All recorded exit codes were zero and EXE SHA-256 was unchanged. Engine logs confirmed rewriting. The source SqlCommand call is virtual DbCommand.ExecuteNonQuery in IL. The extension redirects it to a dedicated helper already built into this test EXE. Matching server, DB, command kind, procedure and budget cause a pre-send exception; mismatch or exhausted budget calls the original API. Earlier unmatched/other-site calls do not consume budget. DB INSERT counts were observed; SQL wire traces were not captured.

--concurrent uses two separate connections and a barrier at the selected site. With budget one, exactly one call threw and the other succeeded; without the extension both succeeded. Budgets reset per process. This fixture does not establish helper delivery to arbitrary existing applications or SqlException-based timeout reproduction.

## Plan-condition trials

| Change | Firings | DB rows |
| --- | ---: | ---: |
| Default | 1 | 3 |
| Server mismatch | 0 | 4 |
| DB mismatch | 0 | 4 |
| Select the other procedure | 1 | 3 |
| Budget 2 | 2 | 2 |
| Budget 0 | 0 | 4 |

Negative budgets were rejected. Concurrent budget-one trials fired once. EXE hashes remained unchanged. Server/DB fields are filters, not application connection overrides. Validated conditions are passed from profiler to helper through process-local environment variables.

## Launcher and live controls

Use PowerShell 7 and Invoke-SqlTarget.ps1 with target, verified JSON, Engine DLL and extension XML; add -Concurrent for concurrent trials. Only the test child receives profiler settings. Results distinguish exit code, acceptance, rejection, IL rewrite, firing count and original output. Acceptance and rejection flags indicate observed records: a later loaded-module check can reject after initial acceptance. Exit zero does not imply firing. Signature-validation bypass is scoped to the local unsigned-extension test child.

Recorded launcher trials: default fired once; zero budget rewrote without firing; negative budget rejected without rewriting/firing.

For -ControlSequence call,enable,call,disable,call,enable,call,call with budget two: initial disabled call succeeded; enabled call threw; disabled call succeeded without consuming budget; reenabled call threw; exhausted-budget call succeeded. Disabling changes helper firing decisions, not IL restoration; reenabling does not reset budget. One PID completed with exit zero, two firings and unchanged hash. Commands/calls are serialized in a dedicated mode. Independent-agent connections, disable/inflight races and arbitrary service controls remain unverified.

## Multiple sites

Two-site plan: Selected budget two and Other budget one produced 2 and 1 firings for the same procedure; another procedure succeeded and DB rows were one. Logs carry methodToken; FiringsByMethod aggregates per site. Duplicate targets rejected the whole plan without rewriting/firing. Single-rule plans still worked and binary hashes matched.

Three-site plan: Selected/#ConfirmOrder budget two, Other/#ConfirmOrder budget one, Third/#OtherOrder budget one, run with -ThreeSites. One PID produced 2,1,1 firings, mismatches/exhausted calls succeeded, DB rows were two and EXE hashes matched.

## Dedicated service on ChaosTest

Register SqlTarget.exe --service as DotNetFaultInjectionAgentProbe in a test guest. Generate/verify one Program.Selected rule with budget one, and match the transferred EXE hash. Put profiler settings in the service's Environment REG_MULTI_SZ under HKLM:/SYSTEM/CurrentControlSet/Services/DotNetFaultInjectionAgentProbe. Include DOTNET_FAULT_INJECTION_PLAN, DOTNET_FAULT_INJECTION_SERVICE_PROBE=1 and DOTNET_FAULT_INJECTION_DIAGNOSTICS. Start with an absolute test-log path, then stop and remove registration.

The guest initially could not acquire the metadata dispenser. This dedicated service mode therefore supports one rule and checks loaded-module MVID, method and call instruction. Without DB connection, the service calls twice: baseline reaches the original closed-connection error twice; injection yields one TimeoutException followed by the original error. SCM Running, plan acceptance, rewriting and one firing were observed; registration was absent after cleanup. This does not establish arbitrary existing-service support.

## Service with a real test DB

With local SQL Server 2022 Express, pass a server such as .\SQLEXPRESS as the second start argument. Use matching plan.server, database=tempdb, procedure=#ConfirmOrder and budget one; verify. The service uses Windows authentication and creates temporary tables/procedures on an open connection.

Recorded baseline: both calls succeeded and DB rows advanced 1,2. Injection: accepted/rewritten, first call threw before send with zero rows; second inserted one row. Hash stayed unchanged; service registration was removed and temporary objects disappeared on connection close.

For overlap mode, generate a 4000 ms delay with budget one and pass log path, server and overlap. A enters the delay before B starts; separate connections insert identified rows into a shared temporary table. The test fails if delay is absent, completion is not B,A, or app/DB counts disagree. Recorded results were B:1/1, A:2/2, with one delay, unchanged hash and service cleanup.

For retry mode, pass log path, server and retry. The service retries once only after the first TimeoutException. Baseline succeeded immediately with one row. Injection produced first-attempt timeout with zero rows, then one successful retry and one row. One firing, unchanged EXE and registration cleanup were confirmed. Duplicate writes after a database commit with a lost response are outside this test.

## File and network faults

Select WriteSelected or ConnectSelected, set conditions and verify. Launch with DOTNET_FAULT_INJECTION_PLAN. These tests use dedicated synchronous EXEs.

| Fault | Condition | Target arguments |
| --- | --- | --- |
| io-error-before-write | File.WriteAllText path | --file, nonexistent path |
| connection-refused-before-connect | TcpClient.Connect host=127.0.0.1 and port | --network, test listening port |
| oom-before-write | Synthetic OutOfMemoryException at selected write | --file, nonexistent path |
| thread-abort-before-write | Actual Thread.Abort in dedicated worker | --file-abort, nonexistent path |

With budget one, IOException and synthetic OOM prevented the first file creation; the second write succeeded; other paths did not fire. TCP first returned ConnectionRefused without reaching the listener; second/different-port connections succeeded. Thread abort ran catch/finally, skipped later worker code and created no file; a second worker's call succeeded.

Synthetic OOM is an exception-handling test, not actual memory exhaustion. Partial writes, post-connect disconnect/lost responses, and applying file/network faults to existing services were not tested.

## Commands and examples

```powershell
dotnet build tests/Fixtures/SqlTarget -c Release
```

```powershell
./tests/Fixtures/SqlTarget/Invoke-SqlTarget.ps1 -Target $exe -Plan $plan -Engine $engine -ExtensionConfig $config
```

```powershell
./tests/Fixtures/SqlTarget/Invoke-SqlTarget.ps1 -Target $exe -Plan $plan -Engine $engine -ExtensionConfig $config -ControlSequence call,enable,call,disable,call,enable,call,call
```
