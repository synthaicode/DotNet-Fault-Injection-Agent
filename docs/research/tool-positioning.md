# Related-tool positioning and README structure

This historical research compared official descriptions, not measured product effectiveness.

| Tool | Value emphasized | Technical context | Sources |
| --- | --- | --- | --- |
| Telerik JustMock | Control DB/web dependencies and test existing code, including static/nonvirtual methods | Official documentation describes .NET Profiling API for advanced commercial mocking | [Introduction](https://www.telerik.com/products/justmock/documentation/introduction), [advanced usage](https://www.telerik.com/products/justmock/documentation/advanced-usage/advanced-usage) |
| Byteman | Change runtime behavior without recompiling; repeat exceptions, synthetic results and rare timing | Java runtime bytecode rewriting, not the same implementation as a .NET profiler | [Official site](https://byteman.jboss.org/) |
| Typemock Isolator | Test legacy code without changing it and control static/nonpublic dependencies | Product messaging was reviewed; current internal APIs were not verified and are not evidence for a profiler implementation | [Product](https://www.typemock.com/isolator-product-page/) |
| Microsoft Dev Proxy | Observe application behavior when APIs fail or delay | HTTP proxy; an example of scenario communication, not a profiler comparison | [Overview](https://learn.microsoft.com/en-us/microsoft-cloud/dev/dev-proxy/overview) |

Our interpretation: explain what hard-to-control dependencies or timing can be tested before describing implementation APIs. Follow with reproducibility, examples, implementation and scope. JustMock emphasizes isolated unit tests; Byteman emphasizes runtime changes and repeatability. Their purposes are not interchangeable.

Position this project as a tool that creates difficult .NET failures/delays at selected sites and conditions for repeatable failure-behavior tests. README order: testing problem, injected condition, observable behavior, target/plan/tool roles, supported scope and samples.

Do not borrow other products' claims as guarantees of this PoC. Do not imply arbitrary return-value editing or source-free support for every binary. Current work focuses on exceptions/delays; some dedicated Windows samples use target-side helpers. Helper-reference-free generic injection has been tested on Linux x64/.NET 8.
