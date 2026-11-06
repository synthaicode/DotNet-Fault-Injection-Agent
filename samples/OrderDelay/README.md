# 注文投入の完了順を逆転させるサンプル

先に来た注文投入だけを待たせ、後から来た処理が先に完了する状態を作ります。
各処理が終わるたびに、画面側の件数とDBの件数が一致するかを確認します。

## 対象プログラム

対象はMicrosoftのWide World Importers注文投入サンプルです。
検証用ホストが実際の画面を動かし、二つのワーカーで注文を投入します。

## この計画で行う注入

`plan.json` は PerformSqlTask 内の ExecuteNonQuery を選びます。
検証用DBへの `Website.InsertCustomerOrders` の呼び出しが条件に一致したとき、送信前に待機させます。
`fault` は `delay-before-send`、`delayMs` は `4000`、`maxFirings` は `1` です。
待機後は元のSQL呼び出しへ進みます。

## 期待する結果

先着のワーカーAが遅延に入り、ワーカーB、Aの順に完了します。
Bの完了後にアプリとDBが1件、Aの完了後に両方2件になります。
発火は一回です。

外部サンプル、Windows / .NET Framework 4.8、隔離したLocalDBが必要です。
この試験ではAgentによる送信前の遅延と、観測区間を分けるためのDB側の待機を併用します。
