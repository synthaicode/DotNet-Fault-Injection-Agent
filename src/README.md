# 配布用の実装

| フォルダ | 内容 | 配布方法 |
| --- | --- | --- |
| Tool | ソース・PDB・バイナリから計画を生成・検証するCLI | NuGetの.NET toolパッケージ |
| Planner | version 3の計画生成・検証 | CLIの依存DLLとして同梱 |
| Shared | version 3の計画モデル | PlannerとRuntimeから共有ソースとしてコンパイル |
| Runtime | 注入された呼び出しを受けて例外・遅延を発火する.NETランタイム | 注入対象と一緒に配置。CLIパッケージとは別 |
| Native/Windows | Windows用CLRプロファイラー拡張 | エンジン・XMLと一式で配置 |
| Native/Linux | Linux用CoreCLRプロファイラー拡張 | エンジン・XMLと一式で配置 |

`dotnet build DotNetFaultInjectionAgent.slnx -c Release` はTool・Planner・Runtimeをビルドします。
ネイティブ拡張は別途ビルドします。Windowsの手順は[こちら](Native/Windows/DEVELOPMENT.md)、Linuxは[こちら](../tests/PlanDrivenInjection/DEVELOPMENT.md)です。

検証対象とホストは [tests](../tests/README.md)、利用者向けの説明は [samples](../samples/README.md) にあります。
NuGetパッケージの範囲と作成手順は [公開準備](../docs/packaging/README.md) を参照してください。
