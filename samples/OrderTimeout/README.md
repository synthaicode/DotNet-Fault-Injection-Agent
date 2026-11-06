# Time out before order submission

## Target

The verification host loads Microsoft Wide World Importers order-insert EXE and runs its actual form and worker without changing the sample source.

## Plan or intervention

The plan selects ExecuteNonQuery in PerformSqlTask. server/database identify an isolated LocalDB and procedure is Website.InsertCustomerOrders. fault is timeout-before-send and maxFirings is 1.

## Expected observations

The worker stops, TimeoutException is recorded internally and no orders are written. In the recorded run, no error dialog appeared during 2.5 seconds after stopping and the button remained Stop Now. Observe whether the failure reaches the user.

## Environment and limits

Requires the external sample binary, matching source/PDB, Windows / .NET Framework 4.8 and LocalDB. This is a hosted test, not a standalone execution of the sample EXE.

See [preparation and execution](../OPERATIONS.md).
