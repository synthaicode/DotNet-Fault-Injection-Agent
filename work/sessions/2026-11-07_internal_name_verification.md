# 2026-11-07: 内部名の変更と実動作検証

## 変更

DotNet Fault Injection Agentの名前空間、ランタイム、計画ツール、Windowsサービス、注文投入Bridge、環境変数、起動スクリプト、Linux拡張・イメージ名を変更した。関連するコード、設定、サンプル準備と手順を同時に更新した。

計画ツールはこのリポジトリから `DotNetFaultInjectionAgent.Tool` 0.1.0-preview.4をローカルpack・installし、`dotnet-fault-injection` で使用した。従来のSourceMap引数順を維持し、サンプル準備が使うverb-first形式も受け付ける。NuGet.orgへの公開は行っていない。

互換性と切替手順は[内部名と互換性](../../docs/development/naming-compatibility.md)に記載した。

## 実行結果

| 検証 | 結果 |
| --- | --- |
| Windows/Linuxネイティブ拡張の再ビルド | 成功。Engineヘッダーの既存警告・エクスポート警告あり |
| 新パッケージの作成・インストール | 成功。v3 capabilities、旧形式の候補列挙・計画生成・照合、サンプル配布先にコピーしたCLIの起動を確認 |
| 全サンプルの準備 | 注文投入用の外部プロジェクトを指定して成功 |
| Linux x64 / .NET 8.0.20 | 既存17ケースと旧DLL同居1ケースの計18ケース成功 |
| 配布済みLinuxサンプルの起動スクリプト | 条件一致で12呼出し中1回のIOException、条件不一致で0回。計画受理・IL書換えも確認 |
| Windowsサンプル7種類 | 戻り値42→-1→42、CoreFileTarget、ファイル障害、TCP、SQL、注文タイムアウト、注文遅延に成功 |
| 注文タイムアウト | 新Bridgeの読み込みと新例外メッセージの照合に成功、DB件数0 |
| 注文遅延 | 遅延1回、非遅延→遅延の完了順、アプリ0→1→2件、DB1→2件 |
| VMの新名SQLサービス | 基準実行は接続未開放エラー2回。注入時はTimeoutException1回→接続未開放エラー。計画受理・IL書換え・Runningを確認 |
| VMの新名共有フラグサービス | Running、ordinary/volatileともfalse→trueを確認 |
| 後処理 | 試験対象バイナリのSHA-256不変、VMの2サービスの停止・登録解除を確認。転送用HTTPサーバーも終了 |
| 静的確認 | PowerShell構文、Markdown相対リンク、git diff --checkに成功 |

ホストSDKは10.0.301。VMはWindows 10.0.26100.0のChaosTest。VM操作はユーザーの指示により画面操作で実施した。

## 証跡

- [Linuxの18ケース](2026-11-07_internal-name-verification/linux-summary.json)
- [Windowsサンプル7種類](2026-11-07_internal-name-verification/windows-summary.json)
- [VMサービスの実測ログと登録解除](2026-11-07_internal-name-verification/service-result.json)

詳細ログは検証作業コピー内の `work/rename-validation/windows-56a430335d954776a0a9c71c6f647762`、`poc/scenarios/PlanDrivenInjection/artifacts/verify-a2d4d8e9cab54ff2891afdf4361a9fba` に保持した。生成物・バイナリはGitに含めていない。

検証した成果物のSHA-256:

| 成果物 | SHA-256 |
| --- | --- |
| Windows Rewrite.dll | `C17E470E33FB0981D437F968E5E688204BD863628D6AA9A5808F32ECC735ADA4` |
| Linux libDotNetFaultInjectionAgent.so | `9405534D368638E52A8491FD8AB6C70A879DA608AD005E1518160482DF61A1AB` |
| DotNetFaultInjectionAgent.Runtime.dll | `1C9F0B8F4EA4B3AE4895269495C7F4D090C09E17D5446596CD5C415D251EADED` |
| DotNetFaultInjectionAgent.OrderBridge.dll | `C6B56A4E54F9237ED0D65F4E0986B0E6AE84ABB5EBD127919B660CEF20670430` |

Engineソースのコミットは `0055c6fce2fe16770f5908ef14cfd7f8a42c2fab`。

## 試験手順の修正と境界

初回のサービス試験スクリプトは既定の配置先解決で失敗した。明示パスで実行するとサービス試験自体は完了したが、PowerShell 5.1のGet-Content由来の付加プロパティを含むJSON化で結果保存が停滞した。ログ読込をFile.ReadAllTextへ変更し、配置先の既定値処理も修正して再実行した。上記サービス証跡は修正版の成功実行によるもの。

サービスの注入試験は閉じた接続を使用する専用PoCで、実DBへのサービス接続は行っていない。実DBの注入・遅延はホストのLocalDBを使用する通常プロセスで検証した。既存業務サービスのインプレース移行、署名済み拡張、NuGet.org配布、Windowsでの汎用version 3注入は未検証・未実施。旧環境変数や旧CLI名の別名互換は提供しない。
