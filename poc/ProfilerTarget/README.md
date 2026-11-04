# プロファイラー起動確認用プログラム

.NET Framework 4.8を対象とするx64のコンソールアプリです。Portable PDBを出力します。開発環境には.NET SDKと.NET Framework 4.8のターゲティングパックが必要です。

リポジトリのルートから実行します。

```powershell
dotnet build poc/ProfilerTarget -c Release
& ./poc/ProfilerTarget/bin/Release/net48/ProfilerTarget.exe
```

プロファイラーなしの基準出力です。

```text
64-bit process: True
GetValue: 42
```

次の検証では、同じEXEを対象にプロファイラーのロードを確認し、GetValueの戻り値をメモリ上のIL書換えで変更できるかを試します。プロファイラーのロード、IL書換え、SQL呼び出しへの介入は未検証です。
