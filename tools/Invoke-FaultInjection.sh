#!/usr/bin/env bash
set -euo pipefail
runtime="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -lt 4 || "$1" != --target || "$3" != --plan ]]; then
  echo 'Usage: bash Invoke-FaultInjection.sh --target /absolute/app.dll --plan /absolute/plan.json [-- app arguments...]' >&2
  exit 1
fi
target="$(realpath "$2")"
plan="$(realpath "$4")"
shift 4
if [[ "${1:-}" == -- ]]; then shift; fi
destination="$(dirname "$target")/DotNetFaultInjectionAgent.Runtime.dll"
if [[ -e "$destination" ]]; then
  cmp --silent "$runtime/DotNetFaultInjectionAgent.Runtime.dll" "$destination" || {
    echo 'A different managed injection runtime already exists beside the target.' >&2; exit 1;
  }
else
  cp --no-clobber "$runtime/DotNetFaultInjectionAgent.Runtime.dll" "$destination"
  cmp --silent "$runtime/DotNetFaultInjectionAgent.Runtime.dll" "$destination"
fi
export CORECLR_ENABLE_PROFILING=1
export CORECLR_PROFILER='{324F817A-7420-4E6D-B3C1-143FBED6D855}'
export CORECLR_PROFILER_PATH="$runtime/libInstrumentationEngine.so"
export MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent="$runtime/Rewrite.xml"
export MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1
export DOTNET_ADDITIONAL_DEPS="$runtime/DotNetFaultInjectionAgent.Runtime.deps.json"
export DOTNET_FAULT_INJECTION_PLAN="$plan"
exec dotnet "$target" "$@"
