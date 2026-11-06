# 注文投入の送信前にタイムアウトを起こすサンプル

注文投入サンプルのSQL送信を一回失敗させ、ワーカーの停止と画面への通知を確認します。

## 対象プログラム

対象はMicrosoftのWide World Importers注文投入サンプルです。
`target` の検証用ホストが、対象EXEを読み込んで本来の画面とワーカーを動かします。
対象サンプルのソースは変更しません。

## この計画で行う注入

`plan.json` は PerformSqlTask 内の ExecuteNonQuery を選びます。
`server` と `database` には隔離したLocalDBの接続条件を指定します。
`procedure` は `Website.InsertCustomerOrders` です。
条件が一致したらSQL送信前に TimeoutException を投げます。
`fault` は `timeout-before-send`、`maxFirings` は `1` です。

## 期待する結果

ワーカーが停止し、内部に TimeoutException が記録され、注文の書込みは0件になります。
過去の検証では、停止後2.5秒の間にエラーダイアログが表示されず、ボタンも Stop Now のままでした。
この挙動を、障害が利用者に通知されるかという観点で観測します。

外部サンプルの配布物と対応するソース・PDB、Windows / .NET Framework 4.8、LocalDBが必要です。
検証用ホスト内での試験であり、対象EXEの単独起動での結果ではありません。
