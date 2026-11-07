# NuGet release preparation

## Distribution scope

Implementation lives in `src/`, verification fixtures and hosts in `tests/`, user examples in `samples/`, plan formats in `schemas/`, and scripts in `tools/`. Generated packages and package-test output are stored under the ignored `artifacts/` directory.

The publishable project is `src/Tool/SourceMap.csproj`, producing `DotNetFaultInjectionAgent.Tool`. `Directory.Build.props` disables packing by default; only Tool enables it. Planner is included as a CLI dependency. Runtime, native extensions and test programs are not separately configured for NuGet publication.

The package includes the CLI, Planner, required managed dependencies including Roslyn, an English README, the AI planning guide, MIT license text and version 3 schema. The Windows x64 Engine, native extension, XML, launcher, checksum manifest and third-party notices are included under the installed tool runtime/win-x64 directory. Users export them with the CLI. Linux execution assets and the generic managed Runtime are also bundled under runtime/linux-x64.

## Build and verify

Maintainer prerequisites: .NET 10 SDK, PowerShell 7, Visual Studio C++ tools with ATL/Windows SDK, CLR SDK headers and nlohmann/json 3.11.3 headers. Users of the published package do not need these build dependencies.

Clone microsoft/CLRInstrumentationEngine and checkout revision 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab. Build and stage the execution payload before packing:

```powershell
./tools/Build-WindowsRuntime.ps1 -EngineSource <pinned-source-directory> -ClrHeaders <cor.h-directory> -JsonHeaders <nlohmann-parent-directory>
dotnet build DotNetFaultInjectionAgent.slnx -c Release
./tools/Pack.ps1
./tools/Test-Package.ps1 -Package ./artifacts/packages/DotNetFaultInjectionAgent.Tool.0.1.0.nupkg
```

Use `./tools/Pack.ps1 -Version <version>` to select a version. Test-Package installs only the specified package into a new tool directory using an empty package cache and a local-only feed. Existing global tools are not changed.

Package metadata is maintained in the Tool csproj. The package README is [nuget-readme.md](nuget-readme.md), and the schema is [injection-plan.v3.schema.json](../../schemas/injection-plan.v3.schema.json).

## Before publication

The project uses the [MIT License](../../LICENSE), also declared through NuGet's SPDX license metadata. Dependencies retain their own licenses.

Confirm the publication version, ownership of the NuGet.org package ID and publishing credentials. The GitHub repository is public, so package users can read the linked documentation. The Windows Engine provenance and notices for this package are documented below. Linux payload build, licenses and platform conditions are described below.

Build release packages from the selected source revision and rerun package verification. The preparation scripts do not push to NuGet.org.

References: [Microsoft .NET tool tutorial](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create), [NuGet pack metadata](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets).

## Windows payload provenance

Build-WindowsRuntime restores the upstream native build dependencies, builds Release x64 with SignType=None and SpectreMitigation=false, compiles this repository's Rewrite.cpp and stages a flat payload. This setting avoids a maintainer dependency on optional Spectre libraries and is recorded in the manifest. Stage-WindowsRuntime accepts the pinned unmodified Engine source revision and copies its own Release output, not a Visual Studio installation binary. Preserve the Engine MIT license, upstream third-party notices and nlohmann/json MIT license in all exports.

The .NET tool pack includes staged runtime files as published tool output, with no duplicate NuGet content/ copy. NU5111 is suppressed for the explicitly exported launcher: it is not a NuGet install.ps1 hook and is never autoexecuted at installation.

Test-Package checks payload completeness, isolated installation, runtime discovery/export, refusal to overwrite, and (on Windows) one injected IOException followed by successful writing in Framework and CoreCLR fixtures, with unchanged target hashes. Fixture compilation is a maintainer verification step, not a user installation requirement.

## GitHub Packages test feed

Test packages are published to `https://nuget.pkg.github.com/synthaicode/index.json`, not NuGet.org. The Publish and verify test package workflow runs on package-test/** branches or manual dispatch. It builds the runtime from pinned source, creates a unique 0.1.0-github.<run-id>.<attempt> version, verifies the local artifact, publishes with repository-scoped GITHUB_TOKEN, and installs again using only GitHub Packages with an empty cache.

The fetched nupkg SHA-256 must equal the locally tested artifact. The workflow then exports the payload and runs actual Framework/CoreCLR injection tests. Package credentials are supplied through the NuGetPackageSourceCredentials_packagefeed environment variable and are not written to the repository or test artifacts. Workflow permissions are contents:read and packages:write. Artifact output includes packages, checksums and verification results, not credentials.

Users downloading private test packages outside Actions need a package-readable credential in their NuGet configuration. Example after configuring that source:

```powershell
dotnet tool install DotNetFaultInjectionAgent.Tool --tool-path ./test-tools --version <test-version> --add-source https://nuget.pkg.github.com/synthaicode/index.json
./test-tools/dotnet-fault-injection.exe runtime export --output ./fault-runtime
```

## Linux payload build and validation

The Linux build job uses src/Native/Linux/Build/Dockerfile (.NET 8 Debian 12 SDK), pinned Engine source and pinned vcpkg baseline 2d318737bc7daa8a872f4469955a0559686f7580. It installs make/jq/clang/LLVM/CMake and native prerequisites. Debian glibc 2.34+ retains libdl.so.2 but lacks the unversioned name explicitly requested by upstream; the build image provides a symlink to the shared system library. No libdl binary is redistributed.

Build-LinuxRuntime.sh runs the upstream build and tests, builds this repository's native extension/managed runtime, stages the payload, records ldd dependencies and rejects unresolved imports. Engine/upstream notices, nlohmann and vcpkg libxml2/utfcpp copyright files are included. The default package requires both staged platform manifests.

Actions first builds the Linux payload and transfers it to the Windows package job. After the required virus scan and publication, Windows and Linux consumers run independently. The Linux consumer downloads the same GitHub package using GITHUB_TOKEN, compares its archive hash and runs the exported Bash launcher. It then runs the independent injection suite with FAULT_RUNTIME_DIRECTORY pointing to the package export; this prevents accidentally testing a separately built Engine/managed runtime. Source projects are only compiled as test targets/planning tools in that verification step.

## Required virus scan

Package creation includes a virus scan. On Windows, Pack.ps1 updates Microsoft Defender definitions and scans the nupkg plus all extracted files; an unavailable scanner, stale definitions, detection or scan error fails the command. Scan-Package.ps1 records the archive SHA-256, scanner/definition version, UTC time and scan log under artifacts/package-scans. It does not delete or remediate files.

Actions uses Pack.ps1 -CandidateOnly to build an unpublished candidate, then transfers that exact archive to the required Ubuntu scan-and-publish job. The job updates ClamAV definitions with freshclam and scans the archive and extracted files. Encrypted content and exceeded scan limits are failures; definitions older than three days are rejected. Publication requires a passing report with the same archive SHA-256. Definition update/scan failures stop publication and both feed-consumer jobs. The package-virus-scan artifact preserves the report and log.

On Linux, update ClamAV definitions successfully before calling Pack.ps1 or Scan-Package.ps1. CandidateOnly is solely an intermediate build step; it is not a successful scanned release. Do not publish such a candidate without scanning it. A clean scan records the scanner's result at that time and is not a guarantee of the absence of malware.

## NuGet.org release 0.1.0

The initial public release is 0.1.0. The manual test workflow accepts package_version to build that exact version and verify it through GitHub Packages before NuGet.org publication. Leave it empty for a unique test version. Both platform feed-consumer jobs and the required virus scan must succeed. Preserve the original nupkg and its SHA-256; do not repack or change metadata after verification.

Publish that verified archive through the authenticated NuGet.org upload page or an appropriately scoped NuGet API key. GITHUB_TOKEN only publishes to GitHub Packages, not NuGet.org. After NuGet.org validation/indexing, install using only https://api.nuget.org/v3/index.json and a new cache, check guide/capabilities/runtime export and run the same platform injection tests. NuGet.org repository signing changes archive bytes; compare all package payload entries against the verified candidate and validate signatures instead of requiring the unsigned candidate's archive hash to match.

The package includes Roslyn's MIT license and ThirdPartyNotices from Microsoft.CodeAnalysis.Common/CSharp 5.0.0 (source revision 6c4a46a31302167b425d5e0a31ea83c9a9aa1d09), in addition to each native runtime's notices. This does not alter the project's MIT license.
