# 実動作の検証

| フォルダ | 内容 |
| --- | --- |
| Fixtures | CoreFileTarget、SqlTarget、ProfilerTarget、SyncServiceの検証対象 |
| CoreFileTarget | 専用CoreCLRファイル注入の検証手順 |
| OrderAgentProbe | 注文投入サンプルを動かすホストとBridge |
| PlanDrivenInjection | 独立したProbeとLinux用の自動注入試験 |

検証用プロジェクトはNuGet公開対象ではありません。
サンプル準備は [tools/Prepare-Samples.ps1](../tools/Prepare-Samples.ps1)、
Windows試験は [tools/Test-RenamedWindowsSamples.ps1](../tools/Test-RenamedWindowsSamples.ps1)、
サービス試験は [tools/Test-RenamedService.ps1](../tools/Test-RenamedService.ps1)を使います。

Linux試験はリポジトリのルートで次を実行します。事前に[ネイティブ拡張とエンジン](PlanDrivenInjection/DEVELOPMENT.md)を `src/Native/Linux/bin` に用意します。

```powershell
python tests/PlanDrivenInjection/verify.py dotnet-fault-injection-runtime:local
```

パッケージの内容・新規インストール・計画生成と照合は [tools/Test-Package.ps1](../tools/Test-Package.ps1) で検証します。
