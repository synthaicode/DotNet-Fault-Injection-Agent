# 2026-11-07: Repository organization for NuGet

## Changes

Moved implementation to src/Tool, Planner, Runtime, Shared and Native/Windows/Linux; verification fixtures/hosts to tests/Fixtures, OrderAgentProbe, CoreFileTarget and PlanDrivenInjection; version 3 schema to schemas/; Windows launcher to tools/. Generated packages and package-verification output use ignored artifacts/.

Added a managed solution, Tool-only pack selection, Pack.ps1 and Test-Package.ps1. Included NuGet README/schema and updated metadata, project references, links and scripts. Historical session facts were retained.

## Verification

- Solution build passed with zero errors/warnings.
- Pack.ps1 passed without a missing-README warning.
- Exported tracked files only to a separate directory, repacked and installed using a new local feed/empty cache. Capabilities and actual Probe plan generation/verification passed. [Package evidence](2026-11-07_nuget-layout/package-summary.json)
- All sample deployments, including external orders, prepared with the new package.
- Seven Windows samples passed. [Evidence](2026-11-07_nuget-layout/windows-summary.json)
- Linux native rebuilt from its new location; 17 existing cases plus old-DLL coexistence passed. [Evidence](2026-11-07_nuget-layout/linux-summary.json)
- PowerShell syntax, Markdown links, shared-source/project references and diff checks passed.

Windows native binaries came from the preceding rename verification with identical source and were copied to the new location. Service runtime tests were not rerun in this layout phase; distinguish the preceding service evidence.

## Remaining items at that time

No NuGet.org publication. License was then unspecified; release version, ID ownership/authentication and documentation access outside the private GitHub repository remained unresolved. This change did not configure Native/Runtime NuGet distributions. Later MIT selection is a subsequent decision, not a change to this historical state.
