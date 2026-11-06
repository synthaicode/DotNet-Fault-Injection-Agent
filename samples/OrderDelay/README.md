# Reverse order-submission completion

## Target

The verification host runs the real Wide World Importers form with two workers.

## Plan or intervention

The plan selects ExecuteNonQuery in PerformSqlTask for Website.InsertCustomerOrders in the test database. fault is delay-before-send, delayMs is 4000 and maxFirings is 1. The original SQL call runs after the delay.

## Expected observations

Worker A arrives first and is delayed; B completes before A. After B completes, application and DB counts are 1; after A completes, both are 2. One fault fires.

## Environment and limits

Requires the external sample, Windows / .NET Framework 4.8 and isolated LocalDB. The test combines an agent pre-send delay with database-side waiting to isolate the observation interval.

See [preparation and execution](../OPERATIONS.md).
