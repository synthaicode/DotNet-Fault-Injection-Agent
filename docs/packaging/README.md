# NuGet公開の準備

## フォルダと公開対象

実装は `src/`、検証対象・ホスト・自動試験は `tests/`、利用者向け説明は `samples/`、計画JSONの形式は `schemas/`、ビルド・配置・検証の入口は `tools/` に置きます。生成パッケージとパッケージ検証出力はGit管理外の `artifacts/` に置きます。

現在の公開対象は `src/Tool/SourceMap.csproj` が作る `DotNetFaultInjectionAgent.Tool` 一つです。
`Directory.Build.props` で全プロジェクトのpackを既定で無効にし、Toolだけで有効にしています。
PlannerはCLIの依存DLLとして同梱されます。Runtime・ネイティブ拡張・テストプログラムを個別NuGetパッケージとして公開する設定はありません。

CLIパッケージには、Tool、Planner、必要なRoslyn等の依存DLL、NuGet用README、version 3スキーマを含めます。プロファイラーエンジン・注入用Runtime・ネイティブDLL/SO・検証対象は含めません。CLIのインストールだけでは障害注入を実行できません。

## ビルドとパッケージ検証

.NET 10 SDKとPowerShell 7を使用し、リポジトリのルートで実行します。

```powershell
dotnet build DotNetFaultInjectionAgent.slnx -c Release
./tools/Pack.ps1
./tools/Test-Package.ps1 -Package ./artifacts/packages/DotNetFaultInjectionAgent.Tool.0.1.0-preview.4.nupkg
```

バージョンを明示する場合は `./tools/Pack.ps1 -Version <version>` を使います。
Test-Packageは新しい検証ディレクトリとパッケージキャッシュを作り、指定したnupkgだけをインストール元に使います。既存のグローバルツールを変更しません。

パッケージメタデータはToolのcsproj、パッケージ内READMEは [nuget-readme.md](nuget-readme.md) が正本です。
スキーマは [schemas/injection-plan.v3.schema.json](../../schemas/injection-plan.v3.schema.json) が正本です。

## 公開前の未決事項

- ライセンスの指定がありません。ライセンス方針を決定し、リポジトリのLICENSEとパッケージのlicenseメタデータへ反映する必要があります。今回の構成整理ではライセンスを選択していません。
- NuGet.orgのパッケージID使用権限、公開アカウント・認証、公開バージョンは実際の公開前に確認します。
- 現在のGitHubリポジトリは非公開です。公開NuGetの利用者に示すドキュメントの閲覧先を決める必要があります。
- ネイティブエンジンを含む配布を追加する場合は、その配布範囲・依存物・署名・ライセンスを別途確認します。

この手順はパッケージ作成とローカル検証までです。NuGet.orgへのpushは行いません。

設定の参照: [Microsoftの.NET tool作成手順](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create)、[NuGetのpackメタデータ](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets)。
