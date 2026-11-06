# DotNet Fault Injection Agentの内部名と互換性

製品の表示名は「DotNet Fault Injection Agent」、GitHubリポジトリ名は `DotNet-Fault-Injection-Agent` です。2026-11-07の資料更新では、実行時の識別子は変更していません。

## 参照関係の確認結果

| 維持する内部名 | 確認した用途・変更時の影響 |
| --- | --- |
| `WindowsChaosAgent.Runtime` と名前空間 `WindowsChaosAgent` | Runtime.csprojのアセンブリ名、Native/Profiler.cppのアセンブリ・型参照、PlannerとSourceMapの呼び出しに使われています。一部だけ変更すると参照が一致しません。 |
| `WindowsChaosAgent.Runtime.dll` / `.deps.json` | サンプル準備、Linux実行スクリプト、既存の検証コード、配置手順が参照しています。新旧バイナリの混在を含む検証が必要です。 |
| `WindowsChaosAgent.Tool` / `dotnet-chaos.exe` | サンプル準備は既に配置されたツールを要求します。このリポジトリだけでは配布パッケージの改名と移行を検証できません。 |
| `WindowsChaosAgentProbe` | Windowsサービス名と、そのEnvironmentレジストリ設定の対象です。既存サービスの移行手順と実機確認が必要です。 |
| `Windows Chaos Agent: timeout before SQL send.` | OrderAgentProbeの例外発生側とHostの結果照合が同じ文字列を使用します。変更時は両者と実行結果の確認が必要です。 |
| `Windows Chaos Agent test rewrite` | プロファイラー設定のNameです。設定読み込みを含む実行確認をしていないため維持しています。 |

## 内部名を変更する前の検証

- ネイティブプロファイラー、Runtime、Planner、SourceMapを一緒に再ビルドし、Linuxの既存注入試験を実行する。
- サンプルの準備・配置・実行を新しい成果物名で確認する。
- Windowsサービスの登録、環境設定、起動と停止、およびOrderAgentProbeの例外照合を実機で確認する。
- 配布済みツールと既存配置に対する互換性・移行方法を決める。

今回行ったのはソースと手順の参照関係の調査です。内部名変更後の動作や、上記の実行試験は未検証です。

このリポジトリにはAGENTS.md、Skill、専用の読み込み手順・改名ワークフローはありません。今後改名手順を定める場合はワークフロー定義、読み込み順を定める場合はAGENTS.mdに置く候補となります。
