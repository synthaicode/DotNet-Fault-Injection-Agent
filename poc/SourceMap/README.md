# ソース上の呼び出しと配布EXEの照合

SqlTarget専用の読み取りツールです。.NET 10 SDK同梱のRoslynと.NET Framework 4.8の参照アセンブリを使い、単一ソースファイルの同期SQL呼び出しを解析します。

```powershell
dotnet build poc/SqlTarget -c Release
$refs = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$source = 'poc/SqlTarget/Program.cs'
$exe = 'poc/SqlTarget/bin/Release/net48/SqlTarget.exe'
dotnet run --project poc/SourceMap -- $source $exe $refs list
```

候補の行番号を確認し、最後の引数の list をその番号に置き換えます。対応するPortable PDBとソースのSHA-256チェックサムを照合し、シーケンスポイントと固定のIL形状から対象命令を特定します。利用者は行番号で選択でき、出力にEXEのSHA-256、MVID、メソッドトークン、ILオフセットと呼び出し先が含まれます。

二つの候補が別々のメソッドトークンに対応することを確認しました。ビルド後に変更したソースと、別のEXEのPDBへの差し替えはそれぞれ拒否されました。ソース上の SqlCommand.ExecuteNonQuery は、このEXEでは DbCommand.ExecuteNonQuery の仮想呼び出しです。

候補は単一のreturn文で呼び出す検証用の形に限定しています。実プロジェクトの読み込み、一般的なILの対応付け、AI対話、注入計画の形式、プロファイラーへの受け渡しは未実装です。
