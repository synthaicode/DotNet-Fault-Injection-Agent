# 開発用の実装

利用するサンプルは [samples](../samples/README.md) にあります。
このフォルダには、配布物を作るためのソースと検証用の実装を置いています。

| フォルダ | 管理するもの |
|---|---|
| targets | 検証対象のプログラムのソース |
| tools/SourceMap | ソース・PDB・バイナリから注入計画を作るツール |
| injection/ProfilerRewrite | Windows用プロファイラー拡張のソース |
| scenarios/OrderAgentProbe | 注文投入サンプルを動かす検証用ホストとBridge |
| scenarios/PlanDrivenInjection | Linux用の汎用計画、ランタイム、ネイティブ拡張、自動検証 |

実行用の対象と計画は [Prepare-Samples.ps1](../tools/Prepare-Samples.ps1) で用意します。
詳しいビルド手順と過去の試験結果は、各フォルダの DEVELOPMENT.md を参照してください。
