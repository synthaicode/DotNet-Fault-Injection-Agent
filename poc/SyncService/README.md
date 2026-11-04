# Windowsサービスと共有フラグの検証

.NET Framework 4.8、x64の専用サービスです。コンソールとWindowsサービスで同じ試験処理を呼びます。

```powershell
dotnet build poc/SyncService -c Release
$exe = (Resolve-Path ./poc/SyncService/bin/Release/net48/SyncService.exe).Path
& $exe --console
```

読み手が最初にフラグを読み、その後に書き手がtrueを発行するよう順序を固定します。通常のフィールドでもvolatileフィールドでも、最初の読み取りはfalseで、書き手の終了をJoinで待って再確認するとtrueでした。Joinを含む試験なので、非同期にポーリングした場合のメモリ可視性や最適化の違いは判定していません。書き込み前の一回しか読み取らない制御フローでは、volatileでも通知を拾えません。

## SCMからの起動

専用の非本番環境で、管理者権限のPowerShellから実行します。サービス名が既に使われている場合は中止します。ログにはサービスの実行アカウントが書き込める絶対パスを指定します。

```powershell
$name = 'WindowsChaosSyncProbe'
$log = Join-Path $env:ProgramData ("WindowsChaosSyncProbe-" + [guid]::NewGuid() + ".log")
if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw 'Service name is in use.' }
New-Service -Name $name -BinaryPathName ('"' + $exe + '"') -StartupType Manual
try {
    sc.exe start $name $log
    if ($LASTEXITCODE -ne 0) { throw 'Service start failed.' }
    (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
    Get-Content -LiteralPath $log
}
finally {
    Stop-Service -Name $name -ErrorAction SilentlyContinue
    sc.exe delete $name
}
```

現在の確認範囲はビルドとコンソール実行です。作業セッションは管理者権限を持たないため、SCMでの起動とサービス起動時のAgentのロード・発火は未検証です。
