# DotNet Fault Injection Agent — planning tool

`DotNetFaultInjectionAgent.Tool` creates and verifies fault-injection plans by checking the target binary, matching source and Portable PDB. It exposes the `dotnet-fault-injection` command and requires .NET 10.

```text
dotnet-fault-injection v3 capabilities
dotnet-fault-injection v3 list target.dll Program.cs 22
dotnet-fault-injection v3 plan target.dll Program.cs 22 plan.json
dotnet-fault-injection v3 verify target.dll Program.cs 22 plan.json
```

Version 3 plans support selected external call sites, environment conditions, firing limits, synchronous exceptions and delays. The generic injection runtime has been tested on Linux x64 / .NET 8.

This package contains the planning CLI and its managed dependencies. It does not contain the native profiler engine, native injection extensions, or a ready-to-run injection deployment. Those are built and deployed separately; installing this tool alone does not enable fault injection.

The version 3 schema is included under `schemas/`. The repository documents the dedicated Windows version 1/2 samples and the generic Linux version 3 runtime.

[Source, scope and build instructions](https://github.com/synthaicode/DotNet-Fault-Injection-Agent)

## AI-assisted planning

Ask your development-environment AI assistant to create a plan using this CLI. Supply the deployed binary, matching source and Portable PDB, runtime environment, test question, requested fault, conditions and firing limit. Have it inspect capabilities, list call sites, generate the JSON and verify the final file. Identity fields must come from the tool, not guesses. A verified plan is not evidence of a successful injection run.

The package includes `AI-plan-guide.md`, with a copyable request and planning workflow. Use the same verified JSON for execution with the separately deployed injection runtime.

## Start with the guide

Ask your AI assistant to run `dotnet-fault-injection guide` and read the output before creating a plan. The guide explains the tool, supported workflow, required inputs and verification boundaries. It is embedded in the installed CLI and works offline.

With a repository checkout, the assistant can instead read `src/Tool/AI-plan-guide.md`. The CLI embeds that same file at build time.

```text
First run dotnet-fault-injection guide and read its output.
Alternatively, read src/Tool/AI-plan-guide.md in this repository checkout.
Follow the guide to create and verify a plan for the target and test objective I provide.
If the guide is unavailable, report that before proceeding.
```
