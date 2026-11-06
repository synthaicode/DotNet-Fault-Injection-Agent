# DotNet Fault Injection Agentの内部名と互換性

製品の表示名は「DotNet Fault Injection Agent」、GitHubリポジトリ名は `DotNet-Fault-Injection-Agent` です。内部名も次の対応で移行します。

## 変更対応表

| 旧名 | 新名 |
| --- | --- |
| `WindowsChaosAgent` | `DotNetFaultInjectionAgent` |
| `WindowsChaosAgent.Runtime.dll` / `.deps.json` | `DotNetFaultInjectionAgent.Runtime.dll` / `.deps.json` |
| `WindowsChaosAgent.Tool` / `dotnet-chaos` | `DotNetFaultInjectionAgent.Tool` / `dotnet-fault-injection` |
| `Invoke-Chaos.ps1` | `tools/Invoke-FaultInjection.ps1` |
| `WindowsChaosAgentProbe` | `DotNetFaultInjectionAgentProbe` |
| `WindowsChaosSyncProbe` | `DotNetFaultInjectionAgentSyncProbe` |
| `ChaosOrderBridge` | `DotNetFaultInjectionAgent.OrderBridge` |
| `ChaosOrderAgent_` | `DotNetFaultInjectionAgentOrder_` |
| `WINDOWS_CHAOS_*` | `DOTNET_FAULT_INJECTION_*` |
| `MicrosoftInstrumentationEngine_ConfigPath64_Chaos` | `MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent` |
| `libChaosRewrite.so` | `libDotNetFaultInjectionAgent.so` |
| `windows-chaos-runtime:local` | `dotnet-fault-injection-runtime:local` |

例外メッセージの製品名と、その結果を照合するOrderAgentProbeも同時に変更します。Windows拡張XMLのNameも新名称に合わせます。`Rewrite.dll`、`Rewrite.xml`、プロファイラーのGUID、計画JSON version 1/2/3、イベント名 `chaos.fired` は維持します。

## 移行手順

旧CLI名・環境変数・DLL名の別名互換は提供しません。旧版の構成の一部だけを新しい名前に変更しないでください。

1. 旧版と別の空ディレクトリで、新版のツール・Windows拡張・Linux拡張・共通ランタイムをビルドします。[準備手順](../../samples/OPERATIONS.md)のローカルパッケージをインストールします。NuGet.orgへの公開は別作業です。
2. 同じ新版からXML、ネイティブ拡張、ランタイムDLLとdeps.json、起動スクリプトを一式配置します。対象アプリには新ランタイムへのProjectReferenceを追加する必要はありません。
3. 起動設定を新しい環境変数へ変更します。サンプルEXEを再ビルドした場合は、ハッシュ・MVID・PDBが変わるため計画を再生成・照合します。利用者の既存アプリも、使用する実バイナリに対して計画を照合します。
4. 旧Windowsサービスは、その所有者・実行ファイル・停止可能な状態を確認してから停止・登録解除し、新名で登録します。Environmentレジストリ設定も新サービスへ明示的に設定します。この変更で既存サービスを自動変更しません。
5. 新しい設定で計画受理・IL書換え・発火回数・対象の結果を確認してから切り替えます。ロールバックは保存した旧版一式と旧起動設定を使います。

サービスの移行手順は検証用サービスを想定しています。既存の業務サービスへのインプレース移行や、署名済み拡張の配布は検証範囲に含みません。

## 再現用の検証

- `python tests/PlanDrivenInjection/verify.py`：Linux x64 / .NET 8の既存17ケース。
- 第2引数にDockerイメージ名、第3引数に旧 `WindowsChaosAgent.Runtime.dll` の実ファイルを指定すると、新旧DLLの同居試験を追加します。
- `tools/Test-RenamedWindowsSamples.ps1`：準備済みWindowsサンプル7種類を実行し、発火・対象結果・EXE不変を確認します。注文投入サンプルの準備には外部の検証用プロジェクトが必要です。
- `tools/Test-RenamedService.ps1 -Bundle <試験用配置先>`：管理者権限で専用サービスを登録し、基準実行・一回だけの注入・共有フラグ試験・登録解除を確認します。必要な配置物はSqlTarget.exe、SyncService.exe、verify済みplan.json、Rewrite.dll、Rewrite.xml、MicrosoftInstrumentationEngine_x64.dllです。

実測結果は[改名検証のセッション記録](../../work/sessions/2026-11-07_internal_name_verification.md)に記載しています。
