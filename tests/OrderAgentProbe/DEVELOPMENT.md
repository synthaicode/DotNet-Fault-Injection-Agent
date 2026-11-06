# Pre-send injection into the order sample

This Windows PoC uses isolated LocalDB. The host loads the unchanged Wide World Importers EXE and operates its actual WinForms form and workers. The profiler matches hash, path, loaded MVID, method token and IL offset before redirecting the selected call to Bridge. Bridge checks connection, DB, procedure and firing budget before throwing TimeoutException or delaying.

## Procedure

1. Build Microsoft's order-insert sample for .NET Framework 4.8 Release with Portable PDB. The recorded source revision was 3c71696a3942879f0481f3bd65fe112a40023f18.
2. Build tests/OrderAgentProbe/Host in Release; keep OrderAgentProbe.exe and DotNetFaultInjectionAgent.OrderBridge.dll together. Build the [native extension](../../src/Native/Windows/DEVELOPMENT.md).
3. Choose a unique DB name: DotNetFaultInjectionAgentOrder_ followed by 32 lowercase hexadecimal characters. Generate a plan for PerformSqlTask with server=(localdb)\MSSQLLocalDB, that database, procedure=Website.InsertCustomerOrders, maxFirings=1; verify it.
4. Launch the host with external EXE absolute path, DB name and baseline. Repeat with inject plus profiler configuration, DOTNET_FAULT_INJECTION_PLAN and DOTNET_FAULT_INJECTION_SAMPLE_EXE. Restrict signature-validation bypass to the test child. The host creates and deletes the test DB.

## Recorded timeout result

Baseline inserted an order. Injection logged plan acceptance, IL rewrite and exactly one firing. The worker stopped and internally recorded TimeoutException. During 2.5 seconds afterward no dialog appeared, the button stayed Stop Now, and DB writes were zero. External EXE SHA-256 was unchanged.

This is a hosted test. Standalone sample execution, arbitrary existing services and signed-extension distribution were not established.

## Reverse completion order

After confirming candidate and conditions, generate a plan with New-ConfirmedPlan.ps1 -MaxFirings 1 -DelayMs 4000. fault is delay-before-send and delayMs is 4000 (supported range 1–10000 ms). Verify and run the host in overlap mode.

Two workers run. The first matching call is delayed before the original SQL API; the DB records each session's first order immediately and waits on later loops to keep them outside the observation interval. Recorded results showed one delay, non-delayed then delayed SQL completion, application counts 0→1→2, and DB counts 1→2. EXE hash stayed unchanged, the test DB was removed, and the timeout plan still worked.
