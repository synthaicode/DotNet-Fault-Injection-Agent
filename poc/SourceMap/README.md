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

バージョン1は、EXEのハッシュ、MVID、メソッドトークン、ILオフセット、呼び出し先、障害種別、接続先、DB、プロシージャ名、発火上限を一つのJSONオブジェクトに持ちます。生成時の既定値から server、database、procedure、maxFirings を変更できます。maxFirings は0以上の32ビット符号付き整数の範囲で、0なら発火しません。接続先は大文字と小文字を区別せず、DBとプロシージャ名は区別して比較します。比較にはSqlClientが返す値を使います。既定の障害種別は timeout-before-send です。注文投入サンプルの専用ホストでは delay-before-send も使えます。プロファイラーは計画の形式と実行中EXEのSHA-256を確認し、対象モジュールのパスとMVID、メソッドトークン、呼び出し命令を照合して書換えます。計画の受理、IL書換え、実際の発火は別々に標準エラーへ出力されます。

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

## .NET Frameworkプロジェクトを解析する

最初の引数に .csproj を指定すると、MSBuildがReleaseおよび.NET Framework 4.8として評価したソースファイル、参照、条件付きコンパイル定数を読み込みます。対象EXEも同じ条件でPortable PDB付きでビルドします。現PoCはフレームワーク参照を使うプロジェクトを対象とし、プロジェクト参照やPackageReferenceを検出した場合は止まります。候補選択は一覧に表示された番号で行います。

```powershell
dotnet run --project poc/SourceMap -- $project $exe $refs recommend
dotnet run --project poc/SourceMap -- $project $exe $refs 1 ./order-plan.json
dotnet run --project poc/SourceMap -- $project $exe $refs verify ./order-plan.json
```

recommend は候補を囲む例外処理を表示し、送信前の失敗で観測する挙動を提案します。AIはソースから実際の復旧経路と影響範囲を確認し、試験条件を利用者と確定してから計画の条件値を更新します。生成時の条件は検証用の既定値です。静的解析で示した候補は、障害を実際に起こして確かめる仮説として扱います。

MicrosoftのWide World Importers注文投入サンプルをソース無変更でビルドし、PerformSqlTask の ExecuteNonQuery を候補として特定しました。生成した計画を verify で照合でき、ILオフセットを1ずらした計画は拒否されました。注文投入サンプルは[専用ホストでの注入試験](../OrderAgentProbe/README.md)にも対応しています。

## 同期処理のおすすめ試験

recommend は同期SQL呼び出しに対して、成功応答を遅らせ、待機中の画面や別処理の応答、完了前後の状態変化を確認する試験も提案します。同期APIが見つかったことだけで、UIスレッドで実行されているか、排他や順序に問題があるかは判断できません。

注文投入サンプルのソースとEXEを維持し、検証用ホストから実際のフォームを操作しました。隔離したLocalDBの注文投入プロシージャ内で4秒待機してからINSERTし、正常終了させました。ワーカー1本で、待機中に画面の数値コントロールが応答し、内部注文件数とDB行数は完了前に0、初回完了後に1でした。試験前後のEXEハッシュは一致しました。遅延はDB側で作成しています。Agentの遅延注入と複数ワーカー間の同期は未検証です。

共有の完了件数を更新する複数ワーカーがある場合、recommend は完了順の逆転も試験候補として提示します。先に到着した呼び出しAだけを遅らせ、B、Aの順に成功させます。開始前、Bの完了後かつAの完了前、Aの完了後で件数を照合し、各完了で正確に1件ずつ増えることを合格条件とします。DB側の呼び出しIDと完了順も照合し、観測対象外の呼び出しは試験区間に混ぜません。

注文投入サンプルでは隔離LocalDBで先着のSQLを4秒遅らせ、別々のSQLセッションからの完了順が2番目、1番目となり、アプリの件数が0、1、2と増えました。recommend はこの試験手順と判定基準を出力します。遅延の設定や観測結果の自動判定は行いません。

## おすすめ試験を確認済みの計画にする

recommend で対象箇所、周辺のcatchと代入、介入・観測・合格条件を表示します。利用者と候補および条件を確認した後、PowerShell 7で次を実行します。実際に確認した候補番号と非本番の接続条件を指定します。

```powershell
./poc/SourceMap/New-ConfirmedPlan.ps1 -Source $project -Executable $exe -References $refs -ConfirmedCandidate 1 -Server $server -Database $db -Procedure 'Website.InsertCustomerOrders' -MaxFirings 1 -OutputPlan ./confirmed.json
```

スクリプトは、指定箇所と配布物を照合して計画を生成し、確認済みの条件を設定します。verify の成功後に新規ファイルとして保存します。実行対象はSqlTarget.exeと、専用ホスト内の注文投入サンプルです。

### 注文投入サンプルでの根拠

- 注文投入は186行目。194行目のcatchは停止フラグとエラー情報を更新します。表示の判定は開始ボタンの処理内の98～102行目にあるため、ワーカーが後から失敗すると画面に表示されない可能性があります。送信前に一回失敗させ、ワーカー停止、例外記録、エラー通知、注文書込みゼロを確認します。
- 成功後は191行目から44～50行目の件数更新へ進み、更新はlockで保護されています。完了順を逆転させたときに各完了で正確に一件ずつ増えるかを観測します。送信前の遅延を試す場合は、確認後に -DelayMs を指定し、注文投入サンプルを専用ホストの overlap モードで実行します。
- ワーカーが197～198行目で共有フラグと詳細を書き込みます。開始ボタン側の一回限りの判定より書き込みが遅ければ、フラグが可視になっても再判定は起きません。読み書き順の試験ではその順序を記録し、メモリ可視性の問題と読み直さない制御フローを区別します。volatileの付与・除去や、古い値を強制する注入は未対応です。

ファイルI/OやTCP接続の計画は、一覧からそれぞれの呼び出しを選びます。File.WriteAllTextには path、TcpClient.Connectには host と port を設定し、既定の maxFirings を調整します。対象APIと障害種別の組み合わせは verify が照合します。[対応する障害と実行手順](../SqlTarget/README.md#ファイルioとtcp接続の障害試験)
