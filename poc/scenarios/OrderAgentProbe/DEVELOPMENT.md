# 注文投入サンプルへの送信前注入

Windows上の隔離LocalDBで、注文投入サンプルのSQL送信前に障害を注入するPoCです。サンプルのソースとEXEを変更せず、検証用ホストがEXEをロードして、本来のWinForms画面とワーカーを動かします。

CLR Instrumentation Engineの拡張は、ホスト内で指定したサンプルだけをハッシュ、パス、ロード済みMVID、メソッドトークンとIL位置で照合します。照合後、メモリ上の呼び出し先をBridgeに変更します。Bridgeは接続先、DB、プロシージャ名、発火上限の条件を満たすと、SQL送信前にTimeoutExceptionを投げます。

## 手順

1. MicrosoftのWide World Importers注文投入サンプルを.NET Framework 4.8、Release、Portable PDB付きでビルドします。

   検証時のソースは 3c71696a3942879f0481f3bd65fe112a40023f18 です。
2. `dotnet build poc/scenarios/OrderAgentProbe/Host -c Release` を実行し、OrderAgentProbe.exeとChaosOrderBridge.dllを同じ出力先に保持します。

   [プロファイラー拡張](../../injection/ProfilerRewrite/README.md)もビルドします。
3. ChaosOrderAgent_ に32桁の小文字16進数を付けた一意なDB名を決めます。

   [計画生成](../../tools/SourceMap/DEVELOPMENT.md#おすすめ試験を確認済みの計画にする)でサンプルのPerformSqlTaskを選び、接続先を (localdb)\MSSQLLocalDB、DBをその一意名、プロシージャを Website.InsertCustomerOrders、上限を1とし、verifyを通します。
4. ホストの引数にサンプルEXEの絶対パス、DB名、baselineを渡して通常実行します。

   次に同じ引数の末尾をinjectにし、[プロファイラー起動設定](../../injection/ProfilerRewrite/DEVELOPMENT.md#起動条件と結果)に加えて WINDOWS_CHAOS_PLAN に計画の絶対パス、WINDOWS_CHAOS_SAMPLE_EXE にサンプルEXEの絶対パスを設定して起動します。

   未署名拡張の署名確認無効化を含む起動設定は検証用の子プロセスに限定します。

   ホストが試験用DBを作成し、終了時に削除します。

## 確認結果

通常実行では注文の記録が1件入りました。注入時は計画受理、IL書換え、発火1回をログで確認しました。

ワーカーは停止し、TimeoutExceptionが内部に記録されました。

停止後2.5秒の観測中にダイアログは出ず、ボタンはStop Nowのままで、DBへの書込みは0件でした。サンプルEXEのSHA-256は一致しました。

検証用ホスト内での試験です。サンプルEXEの単独起動、既存Windowsサービス、署名済み拡張の配布は未検証です。

## Agentで完了順を逆転させる

利用者が候補と条件を確認したら、New-ConfirmedPlan.ps1 に -MaxFirings 1 -DelayMs 4000 を指定します。JSONには fault が delay-before-send、delayMs が4000として記録され、verify後に保存されます。delayMs の範囲は1～10000ミリ秒です。

注入対象の起動設定は送信前タイムアウトの試験と同じで、ホストの末尾引数には overlap を渡します。

このモードでは2ワーカーを起動します。Agentは条件に最初に一致した呼び出しだけを送信前に待たせ、その後で元のSQL APIを呼びます。

DB側では各セッションの最初の注文を即座に書き込み、後続のループ呼び出しだけを観測区間から外すために待たせます。

Agentの記録では遅延が1回、SQL完了は遅延なし、遅延ありの順でした。各完了後にアプリの件数は0から1、2へ増え、DBの行数も1件、2件と一致しました。

試験前後のEXEのSHA-256は一致し、試験用DBも削除されました。送信前タイムアウトの計画も引き続き動作しました。
