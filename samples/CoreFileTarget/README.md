# Fail one file write

## Target

CoreFileTarget calls File.WriteAllText from WriteSelected twice for the same file. It catches IOException, reports it and continues.

## Plan or intervention

The plan selects WriteSelected. When the destination equals path, io-error-before-write throws IOException before opening the file. maxFirings is 1.

## Expected observations

The first call throws and creates no file. The second call succeeds and creates the file. Use a destination that does not already exist.

## Environment and limits

Windows x64 / .NET 10. This dedicated PoC includes a fault helper in the target DLL.

See [preparation and execution](../OPERATIONS.md).
