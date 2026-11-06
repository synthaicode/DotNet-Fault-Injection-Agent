# Refuse one TCP connection

## Target

SqlTarget runs in network-test mode and connects twice to a local listening port. This mode does not use SQL.

## Plan or intervention

The plan selects Program.ConnectSelected and TcpClient.Connect. A matching host 127.0.0.1 and port cause a SocketException with ConnectionRefused before connecting. fault is connection-refused-before-connect; maxFirings is 1.

## Expected observations

The first matching connection fails and never reaches the listener. The second succeeds. A different listening port causes neither call to fire.

## Environment and limits

Windows x64 / .NET Framework 4.8. This plan does not simulate disconnection or lost responses after connection.

See [preparation and execution](../OPERATIONS.md).
