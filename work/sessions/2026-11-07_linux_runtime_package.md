# 2026-11-07: Linux execution payload

Added linux-x64 alongside win-x64 in the same NuGet tool package. Runtime export chooses the current x64 OS by default and supports explicit --rid; hashes are checked and existing output directories are rejected. Linux payload includes the source-built Engine, extension/XML, managed runtime/deps.json, Bash launcher, dependency reports and licenses. The launcher adds the managed runtime beside the target if absent, accepts an identical copy and rejects a conflicting file.

Built pinned Engine revision 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab and vcpkg baseline 2d318737bc7daa8a872f4469955a0559686f7580 in a Debian 12/.NET 8 SDK Docker image. Upstream Engine's 13 tests passed. The build image provides the unversioned libdl.so link to system libdl.so.2 required by upstream on newer glibc. No system libdl is bundled. The custom extension depends on system OpenSSL 3. ldd found no missing imports. Engine, nlohmann, libxml2 and utfcpp notices are preserved.

Locally installed the dual-platform package, exported linux-x64 from the CLI and ran the 17 independent injection cases against the exported Engine/managed runtime in the Linux .NET 8 container: all passed. The runtime source build is skipped when an exported payload is selected, so a rebuilt managed runtime cannot silently replace the packaged runtime. Windows package checks also run to protect existing delivery.

The GitHub Packages workflow now builds/transfers the Linux payload, publishes the dual-platform package from Windows, tests the feed on Windows, then installs the same published version on Ubuntu and runs the Linux launcher and independent suite. Remote results are recorded after the run completes.

Scope: Linux x64 glibc, Debian 12 build and .NET 8 target, OpenSSL 3, libstdc++. CLI requires .NET 10. Alpine/musl, ARM64, macOS and all Linux distributions are not claimed. No NuGet.org publication.
