# Fail a write to a selected path

## Target

SqlTarget runs in file-test mode and writes twice to the path supplied as an argument. This mode does not use SQL.

## Plan or intervention

The plan selects Program.WriteSelected and File.WriteAllText. fault is io-error-before-write; maxFirings is 1. path is the destination condition, not the target EXE location.

## Expected observations

The first matching write throws IOException without creating a file; the second succeeds. Passing a destination different from plan.path causes no firing. Use a new file path.

## Environment and limits

Windows x64 / .NET Framework 4.8.

See [preparation and execution](../OPERATIONS.md).
