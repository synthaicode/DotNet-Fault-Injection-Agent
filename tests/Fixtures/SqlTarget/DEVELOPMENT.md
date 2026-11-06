# 同期SQL呼び出し一箇所の介入検証

.NET Framework 4.8向けのx64テストプログラムで、選んだSQL呼び出しの送信前に一回だけ障害を注入できるかを確認します。ローカルの SQL Server LocalDB の MSSQLLocalDB を使い、同一接続内に一時テーブルと二つの一時ストアドプロシージャを作成します。接続終了で一時オブジェクトは破棄されます。

リポジトリのルートで次を実行し、生成された SqlTarget.exe を使います。

```powershell
dotnet build tests/Fixtures/SqlTarget -c Release
```

[拡張のビルドと起動条件](../../../src/Native/Windows/README.md)に従い、プロファイラー無効、選択箇所用の[JSON計画](../../../src/Tool/README.md)と拡張あり、プロファイラー無効の順に、同じEXEを起動します。

| 起動 | 選択した箇所で対象プロシージャ | 別の箇所で対象プロシージャ | 選択した箇所で別プロシージャ | 一致する二回目の呼び出し | DBの記録行数 |
|---|---|---|---|---|---:|
| 無効 | 成功 | 成功 | 成功 | 成功 | 4 |
| 拡張あり | TimeoutException | 成功 | 成功 | 成功 | 3 |
| 無効 | 成功 | 成功 | 成功 | 成功 | 4 |

全起動の終了コードは0で、試験前後のEXEのSHA-256は一致しました。エンジンのログにも選択したメソッドの書換え成功が記録されました。

選択箇所のILでは、ソース上の SqlCommand からの呼び出しが DbCommand.ExecuteNonQuery の仮想呼び出しとして記録されます。拡張はその命令を、テストEXEにあらかじめ組み込んだ専用ヘルパーの呼び出しに置き換えます。

ヘルパーは接続先、DB、コマンド種別、プロシージャ名がJSON計画の条件に一致する場合に、SQL APIに進まず例外を投げます。

既定の計画では一致した最初の一回だけ発火枠を取得します。上限到達後と不一致の場合は元のAPIを呼びます。

別箇所と不一致の呼び出しを先に実行し、発火枠を消費しないことも確認しました。条件不一致の試験ではプロシージャ名だけを変えています。

DB側はプロシージャが行ったINSERTの件数を確認しています。SQL通信のトレースはこの検証では採取していません。

同時実行を試すには、SqlTarget.exe に --concurrent を渡します。別々の接続を使う二つのタスクが、バリアで揃ってから同じ対象箇所を呼びます。

拡張ありでは一方だけが TimeoutException になり、もう一方は正常に終了しました。拡張なしでは両方が正常に終了しました。

発火枠はプロセス起動ごとに初期化されます。

対象は固定のテストメソッドです。既存アプリへのヘルパー提供、Roslynと配布物の対応確認、SqlExceptionによるSQLタイムアウト再現は未実装です。

## JSON計画の条件を変更した試験

同じEXEと同じ呼び出し箇所に対し、各試験でJSONの値だけを一つ変更しました。

| 変更 | 発火回数 | DBの記録行数 |
|---|---:|---:|
| 既定値 | 1 | 3 |
| 接続先を不一致に変更 | 0 | 4 |
| DBを不一致に変更 | 0 | 4 |
| プロシージャ名をもう一方へ変更 | 1 | 3 |
| 上限を2へ変更 | 2 | 2 |
| 上限を0へ変更 | 0 | 4 |

上限が負数の計画は拒否されました。上限1での同時実行では一方だけ発火しました。試験前後でEXEのSHA-256は一致しました。

接続先とDBの条件は照合にのみ使います。JSONを変えてもアプリの接続先は変わりません。

検証済みの条件はプロファイラーから検証用ヘルパーへプロセス内の環境変数で渡します。

## 検証用ランチャー

PowerShell 7で、対象EXE、JSON計画、CLR Instrumentation EngineのDLL、拡張のXML設定を指定します。

```powershell
./tests/Fixtures/SqlTarget/Invoke-SqlTarget.ps1 -Target $exe -Plan $plan -Engine $engine -ExtensionConfig $config
```

同時実行試験には -Concurrent を追加します。

ランチャーは検証用の子プロセスにだけ起動設定を渡します。結果として、終了コード、計画受理と拒否の記録、IL書換え、発火回数、元の出力をそれぞれ返します。

受理後にロード済みモジュールの照合で拒否される場合があるため、受理と拒否はそれぞれ記録の有無を表します。

終了コード0でも、発火したかどうかは発火回数で確認します。

ローカルの未署名拡張を使うため、署名検証の無効化は検証用の子プロセスに限定します。

既定の計画で一回発火、上限0で書換えのみ、負の上限で計画拒否と書換え・発火なしを確認しました。

## 起動中に有効・無効を切り替える試験

Program.Selected を選んで現在のEXE用のJSON計画を生成し、maxFirings を2に設定します。同じ子プロセスへ標準入力でコマンドを順に送り、標準出力で応答を受け取ります。

```powershell
./tests/Fixtures/SqlTarget/Invoke-SqlTarget.ps1 -Target $exe -Plan $plan -Engine $engine -ExtensionConfig $config -ControlSequence call,enable,call,disable,call,enable,call,call
```

| 呼び出し時の状態 | 結果 |
|---|---|
| 起動直後の無効 | SQL実行成功 |
| 有効化後 | TimeoutException |
| 発火枠が1回残った状態で無効化 | SQL実行成功 |
| 再有効化後 | TimeoutException |
| 発火枠を使い切った後 | SQL実行成功 |

有効化と無効化はプロセス内ヘルパーの発火判定に反映されます。無効化は発火枠を消費せず、再有効化は発火枠をリセットしません。

試験は一つのPIDで完了し、終了コードは0、発火記録は2回、試験前後のEXEのSHA-256は一致しました。

制御コマンドとSQL呼び出しは専用の検証モードで直列に処理します。独立したAgentからの接続、実行中の呼び出しと無効化が競合した場合の挙動、既存Windowsサービスの制御は未検証です。

## 二箇所を同じ計画で実行する試験

Selected の上限を2、Other の上限を1に設定し、同じJSON計画で起動しました。同じプロシージャに対して Selected で2回、Other で1回発火し、別プロシージャの呼び出しは成功しました。DBへの記録は1件でした。

書換えと発火のログに methodToken を含め、ランチャーの FiringsByMethod で箇所別の件数を集計します。

重複した対象を含む計画は全体が拒否され、書換えと発火は0回でした。

従来形式の単一ルールは引き続き動作し、試験前後で同じEXEのSHA-256が一致しました。

## 三箇所の試験

SourceMapの候補から Selected、Other、Third を選んで一つのJSONを生成し、ランチャーに -ThreeSites を渡します。

Selected は #ConfirmOrder を2回、Other は #ConfirmOrder を1回、Third は #OtherOrder を1回まで失敗させる条件に設定しました。

同じPIDで箇所別に2回、1回、1回発火し、条件不一致と上限到達後の呼び出しは成功しました。DBへの記録は2件で、試験前後のEXEのSHA-256は一致しました。

## ChaosTestのWindowsサービスで一回だけ発火

SqlTarget.exe を --service 付きで手動起動の専用サービス DotNetFaultInjectionAgentProbe に登録します。

SourceMapで Program.Selected の計画を生成し、maxFirings が1のまま verify を実行します。

ゲストへ転送したEXEのSHA-256も計画に照合します。

プロファイラーの設定は専用サービスのレジストリキー HKLM:\SYSTEM\CurrentControlSet\Services\DotNetFaultInjectionAgentProbe の Environment（REG_MULTI_SZ）に設定します。

[通常の起動設定](../../../src/Native/Windows/DEVELOPMENT.md#起動条件と結果)に加え、DOTNET_FAULT_INJECTION_PLAN に計画の絶対パス、DOTNET_FAULT_INJECTION_SERVICE_PROBE に1、DOTNET_FAULT_INJECTION_DIAGNOSTICS にAgentの診断ログの絶対パスを指定します。

サービスの開始引数には試験ログの絶対パスを渡します。試験後にサービスを停止し、登録を削除します。

ChaosTestでは初期化時のメタデータディスペンサー取得が失敗したため、この専用のサービスモードは一つのルールに限定し、ロード済みモジュールでMVID、メソッドと呼び出し命令を照合します。

サービスは接続を開かずに同じコマンドを二度呼びます。実DBへの接続が不要で、元のAPIに到達したかを通常の接続未開放エラーで判別できます。

| 起動 | 1回目 | 2回目 | Agent発火 |
|---|---|---|---:|
| プロファイラー無効 | 接続未開放エラー | 接続未開放エラー | 0 |
| 検証済み計画あり | 注入したTimeoutException | 接続未開放エラー | 1 |

SCMでRunning、Agentログで計画受理とIL書換え、サービスログで発火1回を確認しました。

停止後に登録がないことも確認しました。

実DBを使う既存Windowsサービスでの動作は未検証です。

### 実DBに接続するサービス試験

SQL Server 2022 Expressのローカルインスタンスを使う場合、サービス開始引数にログの絶対パスと接続先（例 .\SQLEXPRESS）を渡します。計画の server を同じ接続先に設定し、database を tempdb、procedure を #ConfirmOrder、maxFirings を1にして verify を通します。

サービスは接続を開き、一時テーブルと一時プロシージャを作成します。SQL認証情報は使わず、サービス実行アカウントのWindows認証で接続します。

ChaosTestで基準実行は両方成功し、DB件数が1、2と増えました。

同じEXEに計画を適用した実行は、計画受理とIL書換えの後に送信前TimeoutExceptionが1回発火し、初回直後の件数は0でした。

二回目は一行を書き込み、件数は1でした。

試験前後で

EXEのハッシュは一致し、専用サービスは登録を解除しました。一時オブジェクトは接続を閉じると破棄されます。

### 2ワーカーで完了順を逆転するサービス試験

実DBに接続するサービス試験と同じ設定を使い、計画生成時に -DelayMs 4000 -MaxFirings 1 を指定します。サービスの開始引数にはログの絶対パス、接続先、overlap を渡します。

二つのワーカーは別々の接続を使い、AがAgentの送信前遅延に入ってからBを開始します。

各ワーカーは識別子付きの行を共有の一時テーブルに一件挿入し、戻り値とアプリ件数、DB件数を照合します。

試験は遅延が始まらない場合、完了順がB、Aにならない場合、または各完了後の件数が一致しない場合に失敗します。

ChaosTestのSQL Server 2022 Expressで、verify済み計画の受理、IL書換え、送信前遅延1回を確認しました。

Bの完了直後にアプリとDBは1件、Aの完了直後に両方2件でした。EXEのハッシュは一致し、試験サービスを停止して登録を解除しました。

### サービス自身の再試行

実DB試験の開始引数の末尾に retry を追加します。サービスは正常完了した時点で処理を終え、初回にTimeoutExceptionを捕捉した場合に限り、同じSQL呼び出しを一度再試行します。

ChaosTestで、注入なしでは初回に成功し、DB件数は1でした。

発火上限1のverify済み計画を渡した場合は、初回の送信前TimeoutException直後にDB件数0、サービス自身の再試行後にDB件数1でした。

Agent発火は1回で、EXEのハッシュは試験前後で一致し、専用サービスは登録を解除しました。

DB側で既に処理済みの操作を再試行する場合の二重書込みは、この試験の範囲外です。

## ファイルI/OとTCP接続の障害試験

SourceMapの候補一覧から、ファイルなら WriteSelected、TCPなら ConnectSelected を選んでJSONを作成し、条件を設定して verify を通します。

プロファイラーの起動設定は[既存の手順](../../../src/Native/Windows/README.md)を使い、DOTNET_FAULT_INJECTION_PLAN に計画の絶対パスを渡します。いずれも専用EXEでの同期API試験です。

| 障害種別 | 対象と条件 | 試験用EXEの引数 |
|---|---|---|
| io-error-before-write | File.WriteAllText(string,string)、path に対象の絶対パス | --file と未作成の対象パス |
| connection-refused-before-connect | TcpClient.Connect(string,int)、host は127.0.0.1、port は試験用の待受ポート | --network とポート番号 |
| oom-before-write | File.WriteAllText の直前にOutOfMemoryExceptionを模擬。path を指定 | --file と未作成の対象パス |
| thread-abort-before-write | File.WriteAllText の直前に専用ワーカーで実際にThread.Abort。path を指定 | --file-abort と未作成の対象パス |

上限1で、ファイルのIOExceptionと模擬OOMは初回に発火してファイルを作らず、二回目の書き込みは成功しました。別パスでは発火しませんでした。

TCPは初回にConnectionRefusedを返し、待受側に接続は届きませんでした。二回目と別ポートでは接続できました。

スレッド中断はcatchとfinallyが実行され、その後のワーカー処理は実行されず、ファイルは作られませんでした。別スレッドでの二回目の呼び出しは書き込みに成功しました。

OOMは例外処理の試験です。実際のメモリ枯渇、書き込み途中のエラー、接続後の切断や応答喪失、ファイルとネットワークの障害を既存Windowsサービスに適用する試験は未実施です。
