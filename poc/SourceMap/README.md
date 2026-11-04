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

## JSON計画を使って実行する

選んだ行番号の後に新規ファイルのパスを指定します。既存ファイルへの上書きは拒否します。

```powershell
dotnet run --project poc/SourceMap -- $source $exe $refs 101 ./selected.json
```

行番号は候補一覧に合わせます。生成したファイルの絶対パスを、[プロファイラーの起動条件](../ProfilerRewrite/README.md)に加えて検証用の子プロセスの WINDOWS_CHAOS_PLAN に渡します。計画を指定しない場合、SQLへの介入は無効です。

バージョン1は、EXEのハッシュ、MVID、メソッドトークン、ILオフセット、呼び出し先、障害種別、接続先、DB、プロシージャ名、発火上限を一つのJSONオブジェクトに持ちます。生成時の既定値から server、database、procedure、maxFirings を変更できます。maxFirings は0以上の32ビット符号付き整数の範囲で、0なら発火しません。接続先は大文字と小文字を区別せず、DBとプロシージャ名は区別して比較します。比較にはSqlClientが返す値を使います。障害種別は timeout-before-send 固定です。プロファイラーは計画の形式と実行中EXEのSHA-256を確認し、対象モジュールのパスとMVID、メソッドトークン、呼び出し命令を照合して書換えます。計画の受理、IL書換え、実際の発火は別々に標準エラーへ出力されます。

同じEXEで二つの候補から別々の計画を生成し、計画を切り替えるだけで発火箇所が移ることを確認しました。ハッシュまたはMVIDが不一致の場合と、計画がない場合は、どの呼び出しも発火しませんでした。試験前後でEXEのSHA-256は一致しました。

候補は単一のreturn文で呼び出す検証用の形に限定しています。実プロジェクトの読み込み、一般的なILの対応付け、AI対話、外部Agentによる管理、既存サービスでの動作は未実装です。

AIを使って利用者の指定から計画を作る場合は、[AIによる注入計画の作成](AI-plan-guide.md)を参照してください。

## 複数箇所の計画

候補一覧から複数の行を選び、行番号をカンマでつないで渡します。

```powershell
dotnet run --project poc/SourceMap -- $source $exe $refs '172,178,184' ./multi.json
```

行番号は手元の候補一覧に合わせます。出力は version が2のオブジェクトで、rules 配列の各要素に従来のバージョン1の計画が入ります。各要素の server、database、procedure、maxFirings を独立に設定できます。単一行の指定では従来形式を出力します。

PoCで受理するのは、同じSqlTarget.exeにある異なる検証用メソッドへのルールです。計画全体の上限は64 KiBです。プロファイラーはすべてのルールの値、EXEのハッシュ、MVID、対象メソッドを事前照合します。いずれかが不正なら計画を拒否します。実際のIL命令はメソッドごとのJIT時にも確認します。

## 実行前に計画を検証する

```powershell
dotnet run --project poc/SourceMap -- $source $exe $refs verify ./multi.json
```

対象プロセスを起動せずに、JSONの形式、重複キー、値の型と範囲を検査し、すべてのルールをソース、Portable PDB、EXEの対象命令と照合します。成功時は照合したルール件数を表示します。失敗時は理由を表示して終了コード1を返します。接続先やプロシージャの存在および発火は実行試験で確認します。
