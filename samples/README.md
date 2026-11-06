# Fault injection samples

Each README describes the target, intervention and expected observations. Use the same plan.json for planning verification and execution.

| Sample | Test | Environment |
| --- | --- | --- |
| [CoreFileTarget](CoreFileTarget/README.md) | Fail one file write | Windows / .NET 10 |
| [SqlTimeout](SqlTimeout/README.md) | Fail one SQL send | Windows / .NET Framework 4.8 / LocalDB |
| [FileWriteFailure](FileWriteFailure/README.md) | Fail writes to the selected path | Windows / .NET Framework 4.8 |
| [TcpConnectionRefused](TcpConnectionRefused/README.md) | Refuse one connection before connect | Windows / .NET Framework 4.8 |
| [OrderTimeout](OrderTimeout/README.md) | Observe worker failure and UI notification | Windows / external sample / LocalDB |
| [OrderDelay](OrderDelay/README.md) | Reverse completion order | Windows / external sample / LocalDB |
| [PlanDrivenInjection](PlanDrivenInjection/README.md) | Inject without a target-side helper reference | Linux x64 / .NET 8 |

[ProfilerReturnValue](ProfilerReturnValue/README.md) demonstrates a fixed return-value rewrite. [SharedFlags](SharedFlags/README.md) observes shared flags. Neither uses a JSON injection plan.

```text
sample/
  README.md    behavior of this test
  target/      deployed target files
  reference/   planning inputs
  plan.json    one injection plan
  tools/       planning and execution tools
    runtime/   engine, extension and configuration
```

Generate deployments with [Prepare-Samples.ps1](../tools/Prepare-Samples.ps1). Binary hashes and instruction locations vary across builds: use newly generated and verified plans. Generated files are not committed. Sources live under src/ and tests/. Matching PDBs are copied into reference/ and alongside binaries in target/ because the planner currently locates them there.

The two order samples require an external sample project and binary. Without those inputs their deployments and plans are not generated.

See the [design](../CORE_FILE_INJECTION.md), [operations](OPERATIONS.md) and [AI planning guide](../src/Tool/AI-plan-guide.md).
