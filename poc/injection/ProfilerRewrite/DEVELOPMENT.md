# 初回JITでのIL書換え検証

CLR Instrumentation Engineの拡張で、専用のProfilerTargetにある Program.GetValue の先頭命令を置き換え、戻り値の変化を確認するPoCです。

戻り値の試験ではモジュール名とメソッド名で対象を固定します。SQLの試験ではSourceMapが生成したJSON計画から対象を選びます。

## ビルド

ATLとWindows SDKを用意し、x64 Native Tools Command Prompt for Visual Studioを開きます。ビルド時に指定するヘッダーの場所は次のとおりです。

- Engine APIにはMicrosoft CLR Instrumentation Engineのソース内の src/InstrumentationEngine.Api を指定します。
- CLR SDK headersにはCLR SDKの cor.h と corprof.h を含むディレクトリを指定します。

検証時のAPIソースは 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab です。

nlohmann/json v3.11.3の単一ヘッダーを nlohmann/json.hpp としてインクルード用ディレクトリに置きます。 [公式配布物](https://github.com/nlohmann/json/releases/tag/v3.11.3)

出力用ディレクトリを作り、その中で次を実行します。パスは環境に合わせて置き換えます。ビルド後、Rewrite.xmlも同じ出力先にコピーします。

```bat
cl /nologo /std:c++17 /LD /D_WINDLL /EHsc /DUNICODE /D_UNICODE /I"<Engine API>" /I"<CLR SDK headers>" /I"<JSON include root>" "<source directory>\Rewrite.cpp" /Fe:Rewrite.dll ole32.lib mscoree.lib bcrypt.lib /link /EXPORT:DllGetClassObject
```

## 起動条件と結果

ProfilerTargetをReleaseで一度だけビルドします。CLR Instrumentation Engineを有効にする起動では、検証用の子プロセスにだけ以下の環境変数を渡します。

| 環境変数 | 値 |
|---|---|
| COR_ENABLE_PROFILING | 1 |
| COR_PROFILER | {324F817A-7420-4E6D-B3C1-143FBED6D855} |
| COR_PROFILER_PATH と COR_PROFILER_PATH_64 | Visual Studio同梱のMicrosoftInstrumentationEngine_x64.dllの絶対パス |
| MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent | Rewrite.xmlの絶対パス |
| MicrosoftInstrumentationEngine_DisableCodeSignatureValidation | 1 |
| MicrosoftInstrumentationEngine_LogLevel | All |
| MicrosoftInstrumentationEngine_FileLogPath | ログの絶対パス |

未署名の検証用拡張は既定の署名検証で拒否されることを確認しました。署名検証の無効化はこのローカル検証用の子プロセスに限定します。 [Microsoftの拡張設定資料](https://github.com/microsoft/CLRInstrumentationEngine/blob/main/docs/configuration.md)

Visual Studio 2022同梱のエンジン（ファイルバージョン 15.1.0.2022032203）を使用しました。

同じEXEを、プロファイラー無効、拡張あり、プロファイラー無効の順に起動し、出力が 42、-1、42 になることを確認しました。

全起動で終了コードは0、試験前後でEXEのSHA-256は一致しました。エンジンのログで対象メソッドの書換え成功も確認しました。

Windowsサービスでの起動と署名済み拡張の配布は未検証です。同期SQL呼び出しの結果は[SqlTarget](../../targets/SqlTarget/README.md)に記録しています。
