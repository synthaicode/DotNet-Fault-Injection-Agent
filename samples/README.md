# 障害注入サンプル

各サンプルのREADMEで、対象プログラム、注入計画、期待する結果を確認できます。
注入計画は一つの `plan.json` です。計画作成と実行で、同じファイルを使います。

| サンプル | 試すこと | 環境 |
|---|---|---|
| [CoreFileTarget](CoreFileTarget/README.md) | ファイル書き込みを一回だけ失敗させる | Windows / .NET 10 |
| [SqlTimeout](SqlTimeout/README.md) | SQL送信を一回だけ失敗させる | Windows / .NET Framework 4.8 / LocalDB |
| [FileWriteFailure](FileWriteFailure/README.md) | 選んだパスへの書き込みだけを失敗させる | Windows / .NET Framework 4.8 |
| [TcpConnectionRefused](TcpConnectionRefused/README.md) | 接続前に一回だけ接続拒否を起こす | Windows / .NET Framework 4.8 |
| [OrderTimeout](OrderTimeout/README.md) | 注文投入の失敗と画面への通知を観測する | Windows / 外部サンプル / LocalDB |
| [OrderDelay](OrderDelay/README.md) | 注文投入の完了順を逆転させる | Windows / 外部サンプル / LocalDB |
| [PlanDrivenInjection](PlanDrivenInjection/README.md) | 対象にヘルパーを組み込まず計画から注入する | Linux x64 / .NET 8 |

## 初期検証と観測用のサンプル

[ProfilerReturnValue](ProfilerReturnValue/README.md) は戻り値の固定書換え、
[SharedFlags](SharedFlags/README.md) は共有フラグの観測です。この二つは注入計画JSONを使いません。

## フォルダの読み方

```text
サンプル/
  README.md     この計画で何を起こすか
  target/       対象の配布物
  reference/    計画作成に使う資料
  plan.json     注入計画
  tools/        計画作成と実行の道具
    runtime/    エンジン・拡張・設定をまとめた内部部品
```

配布物と計画は、[準備用スクリプト](../tools/Prepare-Samples.ps1)で生成します。
バイナリのハッシュと命令位置が変わるため、別環境の計画を流用せず、生成した計画を使います。
生成物はGitには登録しません。ソースは開発用の `poc` に置き、サンプルの `target` には配布物を配置します。
対応するPDBは資料として `reference` に置きます。現在の計画作成ツールがバイナリの隣で探すため、`target` にもPDBを同梱します。

注文投入の二つは外部サンプルの配布物が必要です。準備時に外部プロジェクトとバイナリを指定します。
未指定の場合、この二つの実行用ファイルと計画は生成しません。

[全体設計](../CORE_FILE_INJECTION.md)と[準備・実行の手順](OPERATIONS.md)は、サンプルの説明と分けています。
