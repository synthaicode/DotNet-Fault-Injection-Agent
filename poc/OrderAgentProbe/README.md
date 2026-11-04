# 注文投入サンプルへの送信前注入

Windows上の隔離LocalDBを使う検証です。サンプルのソースとEXEは変更せず、検証用ホストがEXEをロードして本来のWinForms画面とワーカーを動かします。CLR Instrumentation Engineの拡張は、ホスト内で指定したサンプルだけをハッシュ、パス、ロード済みMVID、メソッドトークンとIL位置で照合します。メモリ上の呼び出しをBridgeへ振り向け、接続先、DB、プロシージャ名、発火上限が一致するときに送信前のTimeoutExceptionを投げます。

## 手順

1. MicrosoftのWide World Importers注文投入サンプルを.NET Framework 4.8、Release、Portable PDB付きでビルドします。検証時のソースは 3c71696a3942879f0481f3bd65fe112a40023f18 です。
2. `dotnet build poc/OrderAgentProbe/Host -c Release` を実行し、OrderAgentProbe.exeとChaosOrderBridge.dllを同じ出力先に保持します。[プロファイラー拡張](../ProfilerRewrite/README.md)もビルドします。
3. ChaosOrderAgent_ に32桁の小文字16進数を付けた一意なDB名を決めます。[計画生成](../SourceMap/README.md#おすすめ試験を確認済みの計画にする)でサンプルのPerformSqlTaskを選び、接続先を (localdb)\MSSQLLocalDB、DBをその一意名、プロシージャを Website.InsertCustomerOrders、上限を1とし、verifyを通します。
4. ホストの引数にサンプルEXEの絶対パス、DB名、baselineを渡して通常実行します。次に同じ引数の末尾をinjectにし、[プロファイラー起動設定](../ProfilerRewrite/README.md#起動条件と結果)に加えて WINDOWS_CHAOS_PLAN に計画の絶対パス、WINDOWS_CHAOS_SAMPLE_EXE にサンプルEXEの絶対パスを設定して起動します。未署名拡張の署名確認無効化を含む起動設定は検証用の子プロセスに限定します。ホストが試験用DBを作成し、終了時に削除します。

## 確認結果

通常実行では注文の記録が1件入りました。注入時は計画受理、IL書換え、発火1回をログで確認しました。ワーカーは停止し、TimeoutExceptionが内部に記録されました。停止後2.5秒の観測中にダイアログは出ず、ボタンはStop Nowのままで、DBへの書込みは0件でした。サンプルEXEのSHA-256は一致しました。

検証用ホスト内での試験です。サンプルEXEの単独起動、既存Windowsサービス、署名済み拡張の配布は未検証です。
