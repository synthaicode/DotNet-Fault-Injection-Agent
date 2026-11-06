# Fail one SQL send

## Target

SqlTarget connects to LocalDB and creates a temporary table and procedure. It invokes the selected site, another site and another procedure.

## Plan or intervention

The plan selects ExecuteNonQuery in Program.Selected. server is the test LocalDB, database is tempdb, procedure is #ConfirmOrder, fault is timeout-before-send, and maxFirings is 1.

## Expected observations

Only the first matching selected call throws TimeoutException before sending SQL. Another site, another procedure and a second matching call succeed. DB rows decrease from the baseline 4 to 3.

## Environment and limits

Windows x64 / .NET Framework 4.8 / LocalDB. Failure before send is distinct from losing a response after database processing.

See [preparation and execution](../OPERATIONS.md).
