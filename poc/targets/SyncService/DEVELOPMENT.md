# Windowsサービスと共有フラグの検証

.NET Framework 4.8、x64の専用サービスで、共有フラグを書き込む時点と読み取る時点の関係を確認します。コンソールとWindowsサービスで同じ試験処理を使います。

```powershell
dotnet build poc/targets/SyncService -c Release
$exe = (Resolve-Path ./poc/targets/SyncService/bin/Release/net48/SyncService.exe).Path
& $exe --console
```

フラグを読む側が最初に読み取り、その後に書く側がtrueを書き込む順序に固定します。通常のフィールドでもvolatileフィールドでも、最初の読み取りはfalseでした。書く側の終了をJoinで待って再確認すると、どちらもtrueでした。

Joinを含む試験なので、非同期にポーリングした場合のメモリ可視性や最適化の違いは判定していません。書き込み前の一回しか読み取らない制御フローでは、volatileでも通知を拾えません。

## SCMからの起動

専用の非本番環境で、管理者権限のPowerShellから実行します。サービス名が既に使われている場合は中止します。ログにはサービスの実行アカウントが書き込める絶対パスを指定します。

```powershell
$name = 'DotNetFaultInjectionAgentSyncProbe'
$log = Join-Path $env:ProgramData ("DotNetFaultInjectionAgentSyncProbe-" + [guid]::NewGuid() + ".log")
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

## ChaosTestでの実測

2026年11月4日、Hyper-VのChaosTestゲスト内で検証プログラムをビルドし、管理者権限で専用サービスを登録しました。

ゲストのコンパイラに合わせてログ出力と短いメソッドの構文を変更し、試験の同期手順は同じにしました。

SCMでRUNNING、終了コード0を確認し、サービスのログに次の結果を確認しました。

```text
ordinary: first read=False; recheck after publication=True
volatile: first read=False; recheck after publication=True
```

初回の状態待ちでエラーが起き、サービスが稼働したまま削除待ちになりました。

その状態では通常の停止要求が通らなかったため、PIDとEXEの絶対パスを照合して専用プロセスを停止し、SCMで登録がなくなったことを確認しました。試験ログはゲスト内に保持しています。

書く側の終了をJoinで待たずにポーリングする場合の可視性は未検証です。Windowsサービス起動時のAgentロードと発火は[専用のSQL検証サービス](../SqlTarget/DEVELOPMENT.md#chaostestのwindowsサービスで一回だけ発火)で確認しました。
