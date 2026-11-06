# DotNet Fault Injection Agent

Turn hard-to-reproduce failures in .NET applications into repeatable tests.

![An AI assistant creates an injection plan; the agent executes it against the target application.](docs/assets/dotnet-fault-injection-agent-overview-en.png)

Describe the behavior you want to test to the AI assistant in your environment. It inspects the target binary and matching source/Portable PDB, identifies a call site, and creates a verified JSON injection plan. The injection tools execute that plan; the user checks application behavior, logs and external state.

## What you can test

- Fail one file write to check exception handling and continuation.
- Throw before a SQL send to check error propagation, notification and retry behavior.
- Delay an earlier operation to observe a different completion order and compare application and database counts.

The target is the application EXE or DLL. A plan specifies the call site, conditions, fault and firing limit. The profiler intervenes in the running process; it does not rewrite the deployed binary on disk. Source and PDB are planning inputs, not fields to bundle into an execution plan.

## Planning CLI

The NuGet package is `DotNetFaultInjectionAgent.Tool`, with command `dotnet-fault-injection`. It requires .NET 10. The package contains the planning CLI and managed dependencies; the native profiler and injection runtime are deployed separately.

```text
dotnet-fault-injection v3 capabilities
dotnet-fault-injection v3 plan target.dll Program.cs 22 plan.json
dotnet-fault-injection v3 verify target.dll Program.cs 22 plan.json
```

See [package preparation](docs/packaging/README.md) for local builds and installation. NuGet.org publication is a separate release step.

## Supported scope

This is a proof of concept. Generic version 3 injection is tested on Linux x64 / .NET 8 and supports synchronous exceptions and delays before selected external calls. Conditions include environment equality, invocation start and firing limits. Argument matching, ReJIT, faulted Task injection and arbitrary return-value replacement are not supported. Windows version 1/2 samples use dedicated fixtures, some with built-in fault helpers. A fixed-method return-value rewrite is available as a separate demonstration.

## Repository

Implementation is in `src/`, fixtures and verification in `tests/`, plan schemas in `schemas/`, user examples in `samples/`, and scripts in `tools/`. Generated files go under `artifacts/`.

- [Implementation](src/README.md)
- [Samples](samples/README.md)
- [Sample preparation and execution](samples/OPERATIONS.md)
- [Naming and migration](docs/development/naming-compatibility.md)
- [NuGet release preparation](docs/packaging/README.md)

## License

The project is licensed under the [MIT License](LICENSE). Dependencies retain their own licenses.

## Ask an AI assistant to create a plan

Tell the AI assistant in your environment which application behavior you want to test and provide the deployed binary, matching source/Portable PDB and execution environment. The assistant uses the CLI to identify and verify a call site, selects supported conditions and faults, and returns one verified JSON plan with the expected observations.

Use the [AI planning guide and copyable request](src/Tool/AI-plan-guide.md). Plan verification and successful fault injection are separate results.

## Start with the guide

Ask your AI assistant to run `dotnet-fault-injection guide` and read the output before creating a plan. The guide explains the tool, supported workflow, required inputs and verification boundaries. It is embedded in the installed CLI and works offline.

With a repository checkout, the assistant can instead read `src/Tool/AI-plan-guide.md`. The CLI embeds that same file at build time.

```text
First run dotnet-fault-injection guide and read its output.
Alternatively, read src/Tool/AI-plan-guide.md in this repository checkout.
Follow the guide to create and verify a plan for the target and test objective I provide.
If the guide is unavailable, report that before proceeding.
```
