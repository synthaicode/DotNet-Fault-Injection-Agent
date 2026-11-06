# Distribution implementation

| Directory | Content | Distribution |
| --- | --- | --- |
| Tool | Binary/source/PDB planning CLI | NuGet .NET tool |
| Planner | Version 3 planning and verification | Managed CLI dependency |
| Shared | Version 3 plan model | Shared source compiled into Planner and Runtime |
| Runtime | Managed exception/delay hook | Deployed with the target, separately from the CLI |
| Native/Windows | Windows profiler extension | Deployed with engine and XML |
| Native/Linux | Linux profiler extension | Deployed with engine and XML |

`dotnet build DotNetFaultInjectionAgent.slnx -c Release` builds Tool, Planner and Runtime. Build native extensions separately using the [Windows](Native/Windows/DEVELOPMENT.md) or [Linux](../tests/PlanDrivenInjection/DEVELOPMENT.md) instructions.

See [verification](../tests/README.md), [samples](../samples/README.md) and [NuGet preparation](../docs/packaging/README.md).
