#!/usr/bin/env bash
set -euo pipefail
engine="$(realpath "$1")"
headers="$(realpath "$2")"
json="$(realpath "$3")"
output="$(realpath -m "$4")"
mkdir -p "$output"
g++ -std=c++17 -DPLATFORM_UNIX -DCOM_NO_WINDOWS_H -DPAL_STDCPP_COMPAT -DBIT64 \
  -I"$engine/src/unix/inc" -I"$engine/src/unix/inc/mincor" -I"$engine/inc/clr/prof" \
  -I"$headers" -I"$json" -shared -fPIC "$(dirname "$0")/Profiler.cpp" \
  "$engine/src/InstrumentationEngine.Api/InstrumentationEngine_i.cpp" \
  -lcrypto -o "$output/libDotNetFaultInjectionAgent.so"
cp "$(dirname "$0")/Rewrite.xml" "$output/Rewrite.xml"
