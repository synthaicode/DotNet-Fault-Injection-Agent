# サンプルの準備と実行

各サンプルのREADMEは、計画の意味と期待する結果を説明するものです。
ここでは配布物の準備と、実行ツールの使い方を説明します。

## 注入計画を作る担当

注入計画は、利用者の環境で使っているAIが作ります。
利用者は対象プログラムと確かめたいことを伝え、AIが計画作成ツールを使って箇所を照合し、
条件、障害、回数を設定した一つのJSONを作成・検証します。
作成した計画で何を起こすかを確認してから、実行に使います。

以下の配布物の準備は、このリポジトリのサンプルを用意する開発用の作業です。
利用者の既存プログラムを注入のためにビルドし直す手順ではありません。

## 配布物の準備

開発環境で `tools/Prepare-Samples.ps1` を実行すると、各サンプルの対象、資料、計画、道具を生成します。
対象のソースをビルドし、作った配布物から計画を生成・照合する作業です。対象アプリは実行しません。

必要なものは、.NET 10 SDK、.NET 8のターゲットパック、.NET Framework 4.8の参照アセンブリ、
計画作成ツール、ビルド済みのWindows用・Linux用ランタイムです。
計画作成ツールはリポジトリのルートで次のように作成・インストールします。

```powershell
dotnet pack src/Tool -c Release -o artifacts/packages
dotnet tool install DotNetFaultInjectionAgent.Tool --version 0.1.0-preview.4 --tool-path .tools --add-source artifacts/packages
.tools/dotnet-fault-injection.exe v3 capabilities
```

この手順はローカルで作成したパッケージを使います。NuGet.orgへの公開を前提にしません。
別のディレクトリへインストールした場合は、`Prepare-Samples.ps1 -ToolDirectory <ツール配置先>` で指定できます。
エンジンと拡張のビルド方法は [Windows](../src/Native/Windows/DEVELOPMENT.md) と
[Linux](../tests/PlanDrivenInjection/DEVELOPMENT.md) の開発用文書にあります。

既存の計画を置き換える場合だけ `-Force` を付けます。
このスイッチは、既に編集したサンプルの計画や生成物を置き換えます。

注文投入のサンプルは、外部プロジェクトとビルド済みEXEを `-OrderProject` と `-OrderBinary` で指定します。
対応するPortable PDBもEXEと同じ場所に用意します。指定しない場合、この二つの配布物は生成しません。

## Windowsでの配置

対象の配布物は `target`、計画作成用の資料は `reference` にあります。
一つの `plan.json` と `tools` 一式を使います。
`tools/runtime` はエンジン、拡張、XMLをまとめた内部部品です。
環境変数とXMLの場所は起動ツールが設定するため、試験ごとに個別編集する必要はありません。

## Windowsでの実行

PowerShell 7を使い、実行したいサンプルのフォルダへ移動します。
以下はCoreFileTargetの例です。記載した相対パスは、すべてそのサンプルフォルダを基準にします。

```powershell
$plan = Get-Content -Raw ./plan.json | ConvertFrom-Json
./tools/Invoke-FaultInjection.ps1 -Target ./target/CoreFileTarget.exe -Plan ./plan.json -RuntimeDirectory ./tools/runtime -TargetArguments $plan.path
```

他のWindowsサンプルは、対象とアプリ用の引数を次のように変更します。

| サンプル | 対象EXE | アプリ用の引数 |
|---|---|---|
| CoreFileTarget | CoreFileTarget.exe | 計画のpath |
| SqlTimeout | SqlTarget.exe | なし |
| FileWriteFailure | SqlTarget.exe | `--file` と計画のpath |
| TcpConnectionRefused | SqlTarget.exe | `--network` と計画のport |
| ProfilerReturnValue | ProfilerTarget.exe | なし。起動ツールにPlanを指定しない |

`TargetArguments` は対象アプリに渡す引数です。注入計画の項目ではありません。
ファイル試験は、計画のpathにまだファイルがないことを確認して開始します。
試験後に同じ計画で再実行すると、既存ファイルを理由に対象が開始を拒否します。

SharedFlagsは注入ツールを使わず、`target/SyncService.exe` に `--console` を渡して実行します。
Windowsサービスへの登録手順は [開発用の説明](../tests/Fixtures/SyncService/DEVELOPMENT.md) にあります。

## 注文投入サンプルの実行

注文投入は検証用ホストを使うため、注入箇所を照合するEXEと、起動するEXEが異なります。
起動ツールのTargetに `target/OrderAgentProbe.exe`、PlanTargetに `target/MultithreadedOrderInsert.exe` を指定します。
Planには `plan.json`、RuntimeDirectoryには `tools/runtime` を指定します。

TargetArgumentsに渡すのは、外部サンプルEXEの絶対パス、計画のdatabase、実行モードの三つです。
モードはOrderTimeoutが `inject`、OrderDelayが `overlap` です。
検証用ホストは、一意な名前の試験用DBを作成し、終了時に削除します。
実DBや既存の業務DBを指定しないでください。

## Linuxでの実行

PlanDrivenInjectionでは、Windows用の起動ツールと計画作成ツールを使いません。
計画作成には `tools/planner/Planner.dll`、実行には `tools/Invoke-LinuxSample.ps1` を使います。
Dockerで `dotnet-fault-injection-runtime:local` イメージを使えることを前提にします。

サンプルフォルダで、次を実行します。

```powershell
./tools/Invoke-LinuxSample.ps1 -Sample .
```

条件不一致を試すときは `-ConditionMismatch` を付けます。
実行結果と、その実行で使った計画は `evidence` に保存します。

## 結果の確認

Windowsの起動ツールは、計画受理、計画拒否、IL書換え、発火回数、対象ファイルの不変を別々に返します。
終了コード0だけで注入成功とは判定しません。各サンプルのREADMEにある結果と照合します。
Linuxでは、計画受理、IL書換え、chaos.fired、各タスクの結果をログで確認します。
