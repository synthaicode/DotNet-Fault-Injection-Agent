# First-JIT IL rewriting

This PoC rewrites the first instructions of Program.GetValue in ProfilerTarget to observe a changed return value. The return-value demonstration selects a fixed module/method; SQL tests select a site using a SourceMap-generated JSON plan.

## Build

Use ATL, Windows SDK and a Visual Studio x64 Native Tools Command Prompt. Engine API headers come from src/InstrumentationEngine.Api in CLRInstrumentationEngine; CLR SDK headers must include cor.h and corprof.h. The tested API revision is 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab. Place nlohmann/json v3.11.3 single-header json.hpp under nlohmann/. Run the command below in an output directory, adjusting include paths, and copy Rewrite.xml beside Rewrite.dll.

## Launch configuration

Build ProfilerTarget once in Release. Set these variables only on the test child process:

| Variable | Value |
| --- | --- |
| COR_ENABLE_PROFILING | 1 |
| COR_PROFILER | {324F817A-7420-4E6D-B3C1-143FBED6D855} |
| COR_PROFILER_PATH and COR_PROFILER_PATH_64 | Absolute path to MicrosoftInstrumentationEngine_x64.dll |
| MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent | Absolute Rewrite.xml path |
| MicrosoftInstrumentationEngine_DisableCodeSignatureValidation | 1 |
| MicrosoftInstrumentationEngine_LogLevel | All |
| MicrosoftInstrumentationEngine_FileLogPath | Absolute log path |

The unsigned test extension was rejected by default signature validation. Disabling validation was restricted to the local test child. See [engine configuration](https://github.com/microsoft/CLRInstrumentationEngine/blob/main/docs/configuration.md).

## Recorded results

Visual Studio 2022 engine version 15.1.0.2022032203 was used. The same EXE produced 42 without profiling, -1 with the extension, and 42 again without profiling. All exit codes were zero, EXE SHA-256 stayed unchanged and engine logs confirmed the rewrite.

At this initial experiment, service startup and signed extension distribution were unverified. Later dedicated service results are recorded in [SqlTarget](../../../tests/Fixtures/SqlTarget/DEVELOPMENT.md) and the session records. This does not establish support for arbitrary existing services.

## Commands and recorded output

```bat
cl /nologo /std:c++17 /LD /D_WINDLL /EHsc /DUNICODE /D_UNICODE /I"<Engine API>" /I"<CLR SDK headers>" /I"<JSON include root>" "<source directory>\Rewrite.cpp" /Fe:Rewrite.dll ole32.lib mscoree.lib bcrypt.lib /link /EXPORT:DllGetClassObject
```
