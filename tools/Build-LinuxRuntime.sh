#!/usr/bin/env bash
set -euo pipefail
engine="$(realpath "${1:?Supply the pinned Engine source directory}")"
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test "$(git -c safe.directory="$engine" -C "$engine" rev-parse HEAD)" = 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab
test -z "$(git -c safe.directory="$engine" -C "$engine" status --porcelain --untracked-files=no)"
# The upstream build also invokes git; allow this specific bind-mounted checkout in the build container.
git config --global --add safe.directory "$engine"
bash "$engine/src/build.sh" x64 Release -j2
headers="$repo/artifacts/linux-headers"
mkdir -p "$headers/nlohmann"
curl -fsSL https://raw.githubusercontent.com/nlohmann/json/v3.11.3/single_include/nlohmann/json.hpp -o "$headers/nlohmann/json.hpp"
output="$repo/artifacts/runtime/linux-x64"
mkdir -p "$output"
bash "$repo/src/Native/Linux/build.sh" "$engine" "$engine/src/InstrumentationEngine.Api" "$headers" "$output"
engine_so="$(find "$engine/out/Linux/bin/x64.Release" -name libInstrumentationEngine.so -type f -print -quit)"
test -n "$engine_so"
cp "$engine_so" "$output/libInstrumentationEngine.so"
dotnet build "$repo/src/Runtime/Runtime.csproj" -c Release --nologo
cp "$repo/src/Runtime/bin/Release/net8.0/DotNetFaultInjectionAgent.Runtime.dll" "$output/"
cp "$repo/src/Runtime/bin/Release/net8.0/DotNetFaultInjectionAgent.Runtime.deps.json" "$output/"
cp "$repo/tools/Invoke-FaultInjection.sh" "$output/"
cp "$engine/LICENSE" "$output/CLRInstrumentationEngine.LICENSE.txt"
cp "$engine/THIRD-PARTY-NOTICES.txt" "$output/CLRInstrumentationEngine.NOTICES.txt"
cp "$repo/LICENSE" "$output/DotNetFaultInjectionAgent.LICENSE.txt"
cp "$repo/docs/packaging/nlohmann-json.LICENSE.txt" "$output/"
for dependency in libxml2 utfcpp; do
  cp "$engine/out/Linux/Intermediate/x64.Release/vcpkg_installed/x64-linux/share/$dependency/copyright" "$output/$dependency.LICENSE.txt"
done
ldd "$output/libInstrumentationEngine.so" > "$output/engine-dependencies.txt"
ldd "$output/libDotNetFaultInjectionAgent.so" > "$output/extension-dependencies.txt"
if grep -q 'not found' "$output/"*-dependencies.txt; then echo 'Missing native dependencies' >&2; exit 1; fi
python3 - "$output" <<'PY'
import hashlib,json,pathlib,sys
root=pathlib.Path(sys.argv[1])
files={p.name:hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in sorted(root.iterdir()) if p.is_file() and p.name!='manifest.json'}
(root/'manifest.json').write_text(json.dumps({'rid':'linux-x64','engineRevision':'0055c6fce2fe16770f5908ef14cfd7f8a42c2fab','buildEnvironment':'Debian 12, OpenSSL 3, glibc x64','files':files},indent=2)+'\n')
PY
