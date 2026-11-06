# Inject without a target-side fault helper

## Target

Probe has no reference to the injection runtime. Multiple tasks write files through Fixture.Selected, report exceptions and file state, and finally write through Fixture.Other.

## Plan or intervention

The version 3 plan selects File.WriteAllText in Fixture.Selected. conditions.environment requires CHAOS_TEST_CASE=enabled. fault.kind is throw, exceptionType is System.IO.IOException, and maxFirings is 1. The shared runtime throws immediately before the selected call.

## Expected observations

Exactly one selected call fails when conditions match; the task that acquires the budget is nondeterministic. Other tasks and Fixture.Other succeed. A mismatching environment value causes no firing.

## Environment and limits

Linux x64 / .NET 8. On Windows, run Linux containers. The plan format and runtime differ from dedicated Windows version 1/2 tools.

See [preparation and execution](../OPERATIONS.md).
