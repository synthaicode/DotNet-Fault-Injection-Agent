# Ask your AI assistant to create an injection plan

Use the AI assistant in your own development environment to inspect the application and run `dotnet-fault-injection`. The CLI performs binary/source/PDB checks; the AI selects a supported test that answers your question. The tool does not contain an embedded AI service.

## Give the assistant the required inputs

Provide the deployed EXE or DLL, its matching Portable PDB, the corresponding source, the execution OS and .NET version, and the behavior you want to observe. Identify the non-production environment, relevant connection or file conditions, the fault, and the maximum number of firings. Values determined only at runtime must remain explicit unknowns until checked.

For example, ask whether a single failed order submission stops the worker and reaches the UI, or whether delaying an earlier operation causes incorrect shared counts.

## Copyable request

Replace the bracketed inputs with actual paths and test conditions:

```text
Create and verify one injection plan for this application using DotNet Fault Injection Agent.

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

Inspect the relevant processing and exception-handling paths. Check supported capabilities
before choosing a plan version. Identify the call site with the CLI and generate the plan
from the actual binary, matching PDB and source. Do not invent hashes, MVIDs, tokens or IL offsets.
Set only supported conditions and faults, then verify the final JSON with the CLI.
If a call site is ambiguous or a required value is unknown, explain what is needed.
Return the verified JSON, the chosen call site and reason, conditions, fault, firing limit,
expected observations and verification output. Distinguish plan verification from an
executed injection test. Use this same JSON for execution.
```

## Planning workflow

1. Inspect the application's normal path, exception handling, recovery and externally visible state. Treat a static finding as a hypothesis to test.
2. Check capabilities and choose a supported runtime/plan version. Windows dedicated PoCs use version 1/2; generic Linux x64 / .NET 8 injection uses version 3.
3. List candidate calls with the CLI. Resolve ambiguous candidates before generating a plan.
4. Generate a plan from the actual target binary, matching source and Portable PDB. Keep generated identity and call-site fields unchanged.
5. Set the requested rule ID, deployment path, supported conditions, fault and firing limit. Verify the final saved JSON again.
6. Explain what the plan proves when executed, and what must be observed in the application and external systems. Recreate and verify the plan when the target binary changes.

For version 3, use these commands with real paths and the selected source line:

```text
dotnet-fault-injection v3 capabilities
dotnet-fault-injection v3 list target.dll Program.cs 22
dotnet-fault-injection v3 plan target.dll Program.cs 22 plan.json
dotnet-fault-injection v3 verify target.dll Program.cs 22 plan.json
```

Version 3 supports environment equality, starting invocation and an atomic firing budget. Faults are supported synchronous exceptions or a bounded synchronous delay before a selected external call. It does not support argument/business-object matching, arbitrary exception construction or faulted Task injection. Do not translate a requested unsupported condition into an apparently equivalent supported condition without explaining the difference.

## Hand off one executable plan

Return one verified JSON together with the target identity, call site, conditions, fault, firing limit and expected observations. Keep source/project/build details as planning context rather than adding them as execution-plan fields.

Plan verification checks the binding and format; it does not show that injection fired or that the application recovered correctly. During execution, distinguish plan acceptance, IL rewriting and actual firing. Check application logs, notifications, retry behavior and database/file state separately. A pre-send exception does not simulate losing a response after a committed database write.

See the [sample matrix](../../samples/README.md) and [command and format details](DEVELOPMENT.md). The native profiler engine, extension and runtime must be deployed separately from the planning CLI.
