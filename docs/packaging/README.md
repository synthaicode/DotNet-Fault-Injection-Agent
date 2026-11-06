# NuGet release preparation

## Distribution scope

Implementation lives in `src/`, verification fixtures and hosts in `tests/`, user examples in `samples/`, plan formats in `schemas/`, and scripts in `tools/`. Generated packages and package-test output are stored under the ignored `artifacts/` directory.

The publishable project is `src/Tool/SourceMap.csproj`, producing `DotNetFaultInjectionAgent.Tool`. `Directory.Build.props` disables packing by default; only Tool enables it. Planner is included as a CLI dependency. Runtime, native extensions and test programs are not separately configured for NuGet publication.

The package includes the CLI, Planner, required managed dependencies including Roslyn, an English README, the AI planning guide, MIT license text and version 3 schema. The profiler engine, injection Runtime and native DLL/SO files are deployed separately. Installing the CLI alone does not enable injection.

## Build and verify

Use .NET 10 SDK and PowerShell 7 from the repository root:

```powershell
dotnet build DotNetFaultInjectionAgent.slnx -c Release
./tools/Pack.ps1
./tools/Test-Package.ps1 -Package ./artifacts/packages/DotNetFaultInjectionAgent.Tool.0.1.0-preview.4.nupkg
```

Use `./tools/Pack.ps1 -Version <version>` to select a version. Test-Package installs only the specified package into a new tool directory using an empty package cache and a local-only feed. Existing global tools are not changed.

Package metadata is maintained in the Tool csproj. The package README is [nuget-readme.md](nuget-readme.md), and the schema is [injection-plan.v3.schema.json](../../schemas/injection-plan.v3.schema.json).

## Before publication

The project uses the [MIT License](../../LICENSE), also declared through NuGet's SPDX license metadata. Dependencies retain their own licenses.

Confirm the publication version, ownership of the NuGet.org package ID and publishing credentials. The GitHub repository is currently private: decide where package users can read the linked documentation. Any future distribution including native engines requires a separate review of that distribution's dependencies, signatures and licenses.

Build release packages from the selected source revision and rerun package verification. The preparation scripts do not push to NuGet.org.

References: [Microsoft .NET tool tutorial](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create), [NuGet pack metadata](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets).
