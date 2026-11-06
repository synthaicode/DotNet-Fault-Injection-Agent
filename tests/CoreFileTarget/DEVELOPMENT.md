# Windows CoreCLR file injection

On Windows x64/.NET 10, inject IOException immediately before the selected file write. Build tests/Fixtures/CoreFileTarget in Release. Give the planner Program.cs, CoreFileTarget.dll and the .NET 10 reference directory under Microsoft.NETCore.App.Ref/ref/net10.0. Select WriteSelected, set path to an absolute nonexistent file and verify.

Build the [Windows extension](../../src/Native/Windows/DEVELOPMENT.md). Set CORECLR_ENABLE_PROFILING=1, CORECLR_PROFILER={324F817A-7420-4E6D-B3C1-143FBED6D855}, and CORECLR_PROFILER_PATH_64 to the engine. XML and plan configuration are the same as the Framework tests. Launch CoreFileTarget.exe with the test path. Verify against the managed DLL, not the apphost EXE.

Recorded Windows results: plan accepted, IL rewritten, one IOException; the first call created no file and the second succeeded. A different path caused both calls to succeed. DLL SHA-256 stayed unchanged. The same engine also passed the 42,-1,42 ProfilerTarget trial.

In the initial Linux investigation, Ubuntu WSL2 x64/.NET 8 loaded libInstrumentationEngine.so from Microsoft.CodeCoverage 18.7.0 and produced initialization logs. The Windows extension uses Win32, ATL and BCrypt; that initial investigation did not verify a Linux build, plan binding, rewriting or firing. Later generic Linux results are documented [separately](../PlanDrivenInjection/DEVELOPMENT.md).

See the [sample](../../samples/CoreFileTarget/README.md).

## Commands and recorded output

```powershell
dotnet build tests/Fixtures/CoreFileTarget -c Release
```
