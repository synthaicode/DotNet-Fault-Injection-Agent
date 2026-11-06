# SQL送信を一回だけ失敗させるサンプル

選んだSQL呼び出しに対して、送信前に TimeoutException を一回起こします。
例外が起きた呼び出しで、DBへの書き込みが行われないことを確認します。

## 対象プログラム

対象は `target` の SqlTarget です。LocalDBに接続し、一時テーブルと一時プロシージャを作ります。
選んだ箇所、別の箇所、別のプロシージャを使って呼び出しを行います。

## この計画で行う注入

`plan.json` は Program.Selected 内の ExecuteNonQuery を選びます。
`server` は検証用LocalDB、`database` は `tempdb`、`procedure` は `#ConfirmOrder` です。
この条件に一致した呼び出しで、送信する前に TimeoutException を投げます。
`fault` は `timeout-before-send`、`maxFirings` は `1` です。

## 期待する結果

選んだ箇所の最初の一致だけが失敗します。別の箇所と別のプロシージャは成功します。
一致する二回目の呼び出しも成功し、DBの記録行数は通常の4件から3件になります。

Windows x64 / .NET Framework 4.8とLocalDBが必要です。
この障害は送信前の例外です。DBで処理した後に応答を失う障害とは分けて扱います。
