# Windows CoreCLRのファイル注入試験

利用用の対象と注入計画は [サンプル](../../../samples/CoreFileTarget/README.md) にまとめています。

Windows x64、.NET 10で、選んだファイル書き込みの直前にIOExceptionを注入する試験です。リポジトリのルートで次を実行します。

```powershell
dotnet build poc/targets/CoreFileTarget -c Release
```

[SourceMap](../../tools/SourceMap/README.md)に渡すのは、次のとおりです。

- このプロジェクトの Program.cs
- ビルドした CoreFileTarget.dll
- .NET 10の参照アセンブリディレクトリ（Microsoft.NETCore.App.Ref の ref/net10.0）

候補からWriteSelectedを選び、JSONのpathを未作成のファイルの絶対パスに設定してverifyを実行します。

[プロファイラー拡張](../../injection/ProfilerRewrite/README.md)をビルドし、Windowsの子プロセスに次の起動環境を設定します。

| 環境変数 | 値 |
|---|---|
| CORECLR_ENABLE_PROFILING | 1 |
| CORECLR_PROFILER | {324F817A-7420-4E6D-B3C1-143FBED6D855} |
| CORECLR_PROFILER_PATH_64 | エンジンの絶対パス |

拡張XMLと計画の指定方法は.NET Framework試験と同じです。CoreFileTarget.exeを直接起動し、試験用ファイルの絶対パスを引数に渡します。計画の照合対象はapphostのEXEではなく管理DLLです。

Windows x64の.NET 10で計画の受理、IL書換え、IOExceptionの発火1回を確認しました。初回はファイルが作られず、二回目は書き込めました。別パスでは両方成功し、DLLのSHA-256は一致しました。

同じエンジンでProfilerTargetの戻り値を42、-1、42に切り替える試験も通りました。

## Linux

Ubuntu WSL2 x64の.NET 8で、Microsoft.CodeCoverage 18.7.0に含まれるLinux用libInstrumentationEngine.soをCoreCLRに読み込ませ、エンジン自身が初期化ログを出すことを確認しました。

Agent拡張はWindows DLLで、Win32 API、ATL、BCryptを使用します。Linux用拡張のビルド、計画照合、IL書換え、発火は未検証です。
