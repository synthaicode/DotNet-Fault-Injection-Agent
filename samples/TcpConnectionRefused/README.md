# TCP接続を一回だけ拒否するサンプル

選んだTCP接続の直前に、接続拒否の例外を一回起こします。
接続できる相手がいる状態で、アプリ側の失敗時の動きを確認します。

## 対象プログラム

対象は `target` の SqlTarget です。ネットワーク試験モードで、ローカルの待受ポートへ接続します。
同じ待受ポートへの接続を二回行います。このモードはSQL接続を使いません。

## この計画で行う注入

`plan.json` は Program.ConnectSelected 内の TcpClient.Connect を選びます。
`host` の `127.0.0.1` と `port` が一致する呼び出しで、接続前に ConnectionRefused の SocketException を起こします。
`fault` は `connection-refused-before-connect`、`maxFirings` は `1` です。

## 期待する結果

最初の一致だけが失敗し、その接続は待受側に届きません。
二回目の接続は成功します。計画のportと異なる待受ポートを引数で渡すと、どちらの接続でも発火しません。

Windows x64 / .NET Framework 4.8用です。接続後の切断や応答喪失は、この計画の対象ではありません。
