# 対象に障害処理を組み込まずに書き込みを失敗させるサンプル

複数の呼び出しがあるプログラムに対し、計画の条件に一致した書き込みを一回失敗させます。
対象プログラムは注入用ランタイムへの参照を持ちません。

## 対象プログラム

対象は `target` の Probe です。複数のタスクが Fixture.Selected でファイルを書き込み、
例外の有無とファイルの状態を出力します。最後に Fixture.Other からも書き込みます。

## この計画で行う注入

version 3 の `plan.json` は Fixture.Selected 内の File.WriteAllText を選びます。
`conditions.environment` の条件は `CHAOS_TEST_CASE` が `enabled` であることです。
`fault.kind` は `throw`、`exceptionType` は `System.IO.IOException`、`maxFirings` は `1` です。
選んだ呼び出しの直前に、共通ランタイムが例外を投げます。

## 期待する結果

条件が一致する実行では、選んだ箇所で一回だけ例外が発生します。
並列処理のどのタスクが発火枠を取るかは固定しません。
残りのタスクと Fixture.Other の書き込みは成功します。
環境変数の値が一致しない実行では発火しません。

Linux x64 / .NET 8用です。Windows上ではLinuxコンテナを使って試験します。
Windows用のversion 1/2の実行ツールとは、計画形式とランタイムが異なります。
