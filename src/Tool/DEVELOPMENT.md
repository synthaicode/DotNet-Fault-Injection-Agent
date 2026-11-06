# Bind source calls to a deployed binary

The legacy entry analyzes dedicated synchronous calls with .NET 10 SDK Roslyn and .NET Framework 4.8 reference assemblies. Build the target with matching Portable PDB. List candidates, then replace list with a selected line number (single-source mode) or candidate number (project mode). Optional output paths create a new plan file; existing files are rejected.

The planner checks PDB identity and source SHA-256, sequence points and supported IL shape. Output binds binary SHA-256, MVID, method token, IL offset and expected callee. The source SqlCommand.ExecuteNonQuery can appear as a virtual DbCommand.ExecuteNonQuery call in IL. Changed source or a PDB from a different binary was rejected in recorded tests; different candidates mapped to different method tokens.

## Legacy version 1/2 execution

Pass the absolute plan path through DOTNET_FAULT_INJECTION_PLAN to the test child process. SQL intervention is disabled when no plan is supplied. Version 1 is one rule. A comma-separated selection emits version 2 with a rules array; one selection retains version 1.

Rules contain target identity, call site, fault, server, database, procedure and maxFirings. maxFirings is a nonnegative signed 32-bit integer; zero disables firing. Server comparisons ignore case; database and procedure comparisons are case-sensitive and use SqlClient-reported values. Conditions filter calls; changing the plan does not change application connection settings. Default fault is timeout-before-send; the dedicated order host also supports delay-before-send.

The native extension validates format, binary hash, module path/MVID, method token and call instruction. Acceptance, rewriting and actual firing are separate stderr observations. Switching between two verified plans moved the firing site without changing the EXE. Hash/MVID mismatch and absent plans produced no firings; binary hashes remained unchanged.

The legacy fixture mode supports constrained call shapes. It does not establish general IL mapping, an embedded AI conversation, independent-agent control or arbitrary service support.

## Multiple sites and verification

Version 2 supports different fixture methods in one SqlTarget.exe, with independent conditions/budgets and a 64 KiB total plan limit. All rules are checked before acceptance; malformed rules reject the plan. The actual IL instruction is checked again at each method's JIT.

verify runs without starting the target. It checks JSON structure, duplicate keys, types/ranges and every rule against source, Portable PDB and binary. Success reports rule count; failure reports a reason and exits 1. Connection/procedure existence and actual firing require runtime tests.

## Project mode

A .csproj input uses MSBuild's Release/.NET Framework 4.8 evaluation for source files, references and conditional constants. Build the EXE with the same settings and Portable PDB. This PoC supports framework-reference projects and stops on ProjectReference or PackageReference. Select by the displayed candidate number.

recommend shows surrounding exception handling and suggests observations for pre-send failure or synchronous delay. Static suggestions are hypotheses; finding a synchronous API does not establish UI-thread execution, lock problems or order bugs. Confirm the application's recovery path and runtime condition values before changing generated test defaults.

The unchanged Wide World Importers order sample yielded PerformSqlTask/ExecuteNonQuery, verified successfully and rejected a one-byte IL-offset change. Dedicated hosted injection is described [here](../../tests/OrderAgentProbe/DEVELOPMENT.md).

## Suggested timing tests and recorded observations

A database-side four-second delay with one worker preserved UI numeric-control responsiveness; application and DB counts were zero before completion and one afterward. The EXE hash was unchanged. That experiment used DB-side waiting rather than agent delay and did not itself verify multiworker synchronization.

For two workers, delay the earlier call A so B completes before A. Compare counts before execution, after B/before A, and after A; each completion should add exactly one. Match DB call IDs and completion order and exclude unrelated calls from the interval. A recorded isolated-LocalDB trial completed second arrival before first and increased application counts 0,1,2. recommend describes the procedure; it does not configure delays or automatically judge results.

## Confirm a plan

After reviewing the candidate and conditions, use New-ConfirmedPlan.ps1 with actual candidate number, non-production server/database/procedure, firing budget and output path. It generates the plan, applies confirmed values, runs verify and only then saves a new file. Use -DelayMs for confirmed order-delay tests and run the dedicated host in overlap mode.

In the examined order source, submission was at line 186, the catch at 194 updated stop/error state, and display checks were in the initial-button handler at lines 98–102. A worker failing later might not notify the UI; test one pre-send failure and observe worker stop, exception record, notification and zero writes. Successful calls proceeded from line 191 into lock-protected count updates at lines 44–50; test one increment per completion when reversing order. Worker writes at 197–198 can occur after a one-time UI check; distinguish visibility from control flow that never rereads. Volatile toggling or forced stale values are unsupported.

File plans select File.WriteAllText with path; TCP plans select TcpClient.Connect with host/port. verify checks supported API/fault combinations. See [fixture details](../../tests/Fixtures/SqlTarget/DEVELOPMENT.md) and the [AI planning guide](AI-plan-guide.md). For generic version 3 commands, see [Linux planning](../../tests/PlanDrivenInjection/DEVELOPMENT.md).

## Commands and recorded output

```powershell
dotnet build tests/Fixtures/SqlTarget -c Release
$refs = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$source = 'tests/Fixtures/SqlTarget/Program.cs'
$exe = 'tests/Fixtures/SqlTarget/bin/Release/net48/SqlTarget.exe'
dotnet run --project src/Tool -- $source $exe $refs list
```

```powershell
dotnet run --project src/Tool -- $source $exe $refs 101 ./selected.json
```

```powershell
dotnet run --project src/Tool -- $source $exe $refs '172,178,184' ./multi.json
```

```powershell
dotnet run --project src/Tool -- $source $exe $refs verify ./multi.json
```

```powershell
dotnet run --project src/Tool -- $project $exe $refs recommend
dotnet run --project src/Tool -- $project $exe $refs 1 ./order-plan.json
dotnet run --project src/Tool -- $project $exe $refs verify ./order-plan.json
```

```powershell
./src/Tool/New-ConfirmedPlan.ps1 -Source $project -Executable $exe -References $refs -ConfirmedCandidate 1 -Server $server -Database $db -Procedure 'Website.InsertCustomerOrders' -MaxFirings 1 -OutputPlan ./confirmed.json
```
