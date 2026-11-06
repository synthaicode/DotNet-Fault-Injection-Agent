# Observe shared-flag read order

## Target

SyncService uses ordinary and volatile fields and runs the same logic as a console program or dedicated Windows service. This sample observes behavior without fault injection.

## Plan or intervention

The reader reads first, the writer then publishes true, and the reader waits for the writer with Join before checking again. No injection plan, profiler or Rewrite.dll is used.

## Expected observations

Both fields read false initially and true after Join. Volatile does not make a one-time read before publication observe a later write.

## Environment and limits

Windows / .NET Framework 4.8. This test does not determine memory visibility when polling without Join.

See [preparation and execution](../OPERATIONS.md).
