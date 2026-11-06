# Rewrite a fixed return value

## Target

ProfilerTarget prints the value returned by GetValue, normally 42.

## Plan or intervention

The shared Rewrite extension rewrites the initial instructions of fixed method Program.GetValue. This early PoC uses no plan.json.

## Expected observations

Without profiling: 42; with the extension: -1; without profiling again: 42. The deployed EXE stays unchanged; only running IL changes.

## Environment and limits

Windows x64 / .NET Framework 4.8.

See [preparation and execution](../OPERATIONS.md).
