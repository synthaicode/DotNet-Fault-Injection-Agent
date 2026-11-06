# Injection planning CLI

The CLI checks the deployed binary against matching source and Portable PDB and creates a JSON injection plan. Select the call site, supported conditions, fault and firing limit. Dedicated Windows tests use version 1/2; generic CoreCLR tests use version 3.

The package is `DotNetFaultInjectionAgent.Tool`; its command is `dotnet-fault-injection`.

```text
dotnet-fault-injection list <source|project> <binary> <references>
dotnet-fault-injection plan <source|project> <binary> <references> <selection> <plan.json>
dotnet-fault-injection verify <source|project> <binary> <references> <plan.json>
dotnet-fault-injection v3 capabilities
```

See [AI-assisted planning](AI-plan-guide.md), [development details](DEVELOPMENT.md), [sample scope](../../samples/README.md) and [package preparation](../../docs/packaging/README.md).
