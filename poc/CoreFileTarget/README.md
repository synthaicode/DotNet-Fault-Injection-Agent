# Windows CoreCLRのファイル注入試験

Windows x64、.NET 10向けです。dotnet build poc/CoreFileTarget -c Release でビルドします。SourceMapにはこのプロジェクトの Program.cs、出力の CoreFileTarget.dll、.NET 10の参照アセンブリディレクトリ（Microsoft.NETCore.App.Ref の ref/net10.0）を渡します。WriteSelectedを選び、JSONのpathを未作成の絶対パスにしてverifyを実行します。

[プロファイラー拡張のビルド](../ProfilerRewrite/README.md)後、Windowsの子プロセスの起動環境に CORECLR_ENABLE_PROFILING=1、CORECLR_PROFILER={324F817A-7420-4E6D-B3C1-143FBED6D855}、CORECLR_PROFILER_PATH_64 にエンジンの絶対パスを設定します。拡張XMLと計画の指定方法は.NET Framework試験と同じです。CoreFileTarget.exeを直接起動し、試験用ファイルの絶対パスを引数に渡します。計画の照合対象はapphostのEXEではなく管理DLLです。

Windows x64の.NET 10で計画の受理、IL書換え、IOExceptionの発火1回を確認しました。初回はファイルが作られず、二回目は書き込めました。別パスでは両方成功し、DLLのSHA-256は一致しました。同じエンジンでProfilerTargetの戻り値を42、-1、42に切り替える試験も通りました。

## Linux

Ubuntu WSL2 x64の.NET 8で、Microsoft.CodeCoverage 18.7.0に含まれるLinux用libInstrumentationEngine.soをCoreCLRに読み込ませ、エンジン自身が初期化ログを出すことを確認しました。Agent拡張はWindows DLLで、Win32 API、ATL、BCryptを使用します。Linux用拡張のビルド、計画照合、IL書換え、発火は未検証です。
