# Ask your AI assistant to create an injection plan

Use the AI assistant in your own development environment to inspect the application and run `dotnet-fault-injection`. The CLI performs binary/source/PDB checks; the AI selects a supported test that answers your question. The tool does not contain an embedded AI service.

## Give the assistant the required inputs

Provide the deployed EXE or DLL, its matching Portable PDB, the corresponding source, the execution OS and .NET version, and the behavior you want to observe. Identify the non-production environment, relevant connection or file conditions, the fault, and the maximum number of firings. Values determined only at runtime must remain explicit unknowns until checked.

For example, ask whether a single failed order submission stops the worker and reaches the UI, or whether delaying an earlier operation causes incorrect shared counts.

## Copyable request

Start by giving the assistant access to this guide. With the CLI installed, ask it to run
`dotnet-fault-injection guide` and read the output. With a repository checkout, ask it to
read `src/Tool/AI-plan-guide.md`. Both contain the same guide, embedded into the CLI at build time.
The CLI route works offline and does not require access to the GitHub repository.

Replace the bracketed inputs with actual paths and test conditions:

```text
First run `dotnet-fault-injection guide` and read its output. If you are working from
a repository checkout instead, read `src/Tool/AI-plan-guide.md` there.
Follow that guide to create and verify one injection plan for the application below.
If neither source is accessible, report the missing guide before proceeding.

Target binary: [absolute EXE or DLL path]
Matching Portable PDB: [path]
Matching source: [path]
Execution environment: [OS, architecture and .NET version]
Test environment: [isolated test deployment]
Question: [application behavior to check]
Requested fault: [exception or bounded delay]
Conditions: [environment values, file/connection conditions supported by this plan version]
Firing limit: [count]
Output plan: [absolute JSON path]

Inspect the relevant processing and exception-handling paths. Check supported target runtimes and operating systems
before choosing an execution route. Identify the call site with the CLI and generate the plan
from the actual binary, matching PDB and source. Do not invent hashes, MVIDs, tokens or IL offsets.
Set only supported conditions and faults, then verify the final JSON with the CLI.
If a call site is ambiguous or a required value is unknown, explain what is needed.
Return the verified JSON, the chosen call site and reason, conditions, fault, firing limit,
expected observations and verification output. Distinguish plan verification from an
executed injection test. Use this same JSON for execution.
```


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

## Planning workflow

1. Inspect the application's normal path, exception handling, recovery and externally visible state. Treat a static finding as a hypothesis to test.
2. Check capabilities and select the application runtime and OS using the table below.
3. List candidate calls with the CLI. Resolve ambiguous candidates before generating a plan.
4. Generate a plan from the actual target binary, matching source and Portable PDB. Keep generated identity and call-site fields unchanged.
5. Set the requested rule ID, deployment path, supported conditions, fault and firing limit. Verify the final saved JSON again.
6. Explain what the plan proves when executed, and what must be observed in the application and external systems. Recreate and verify the plan when the target binary changes.

For version 3, use these commands with real paths and the selected source line:

```text
dotnet-fault-injection capabilities
dotnet-fault-injection dotnet list target.dll Program.cs 22
dotnet-fault-injection dotnet plan target.dll Program.cs 22 plan.json
dotnet-fault-injection dotnet verify target.dll Program.cs 22 plan.json
```

Version 3 supports environment equality, starting invocation and an atomic firing budget. Faults are supported synchronous exceptions or a bounded synchronous delay before a selected external call. It does not support argument/business-object matching, arbitrary exception construction or faulted Task injection. Do not translate a requested unsupported condition into an apparently equivalent supported condition without explaining the difference.

## Hand off one executable plan

Return one verified JSON together with the target identity, call site, conditions, fault, firing limit and expected observations. Keep source/project/build details as planning context rather than adding them as execution-plan fields.

Plan verification checks the binding and format; it does not show that injection fired or that the application recovered correctly. During execution, distinguish plan acceptance, IL rewriting and actual firing. Check application logs, notifications, retry behavior and database/file state separately. A pre-send exception does not simulate losing a response after a committed database write.

See the [sample matrix](../../samples/README.md) and [command and format details](DEVELOPMENT.md). The native profiler engine, extension and runtime must be deployed separately from the planning CLI.
