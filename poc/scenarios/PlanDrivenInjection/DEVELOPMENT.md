# 計画から指定する CoreCLR 呼び出し注入

対象 DLL、呼び出し箇所、発火条件、障害を version 3 の JSON 計画で指定する。
ソース・Portable PDB・DLL を `SourceMap v3` で照合してから計画を作る。

実行時には、ネイティブ拡張が選んだ呼び出しの直前に共通ランタイムの呼び出しを挿入する。
共通ランタイムが計画の条件を判定し、例外または遅延を発生させる。

## 対応範囲

- .NET 8 の外部 `MemberRef` に対する `call` / `callvirt`。ソース行と任意のメソッド名で
  呼び出しを一意に選べる箇所を扱う。曖昧な箇所は拒否する。
- 一つのプロセスに一つの対象モジュール・一つの呼び出し箇所。
- 条件には環境変数の完全一致と、条件に一致した何回目の呼び出しから発火させるかを指定する。
- 障害は指定した型と文言の同期例外、または呼び出し直前の1～10,000 msの同期遅延。
  例外型は `System.InvalidOperationException`、`System.TimeoutException`、`System.IO.IOException`。
- 発火上限は0～100。共通ランタイムが `Interlocked` で発火枠を獲得する。
- Linux x64 CoreCLR で動作検証した。Windows ネイティブ、.NET Framework、ReJIT、
  呼び出し引数や業務オブジェクトの値による条件、失敗した Task を返す注入は対応範囲外。
  `v3 capabilities` で対応内容を取得できる。

注入位置は元の引数評価が終わった呼び出し命令の直前になる。条件や障害を変更する
場合も、ソース・PDB・DLL・呼び出し箇所の照合を維持する。

既存の version 1/2 の PoC は従来の入口を使う。

## ビルド

SourceMap と共通ランタイムをビルドする。プランナーの入口は既存の SourceMap に追加した `v3`。

```powershell
dotnet build poc/tools/SourceMap -c Release
dotnet build poc/scenarios/PlanDrivenInjection/Runtime -c Release
dotnet run --project poc/tools/SourceMap -c Release -- v3 capabilities
```

ネイティブ拡張のビルドには g++、OpenSSL 開発ヘッダー、nlohmann/json 3.11.3、
CLR Instrumentation Engine のソースと生成ヘッダーを用意する。
検証に使った Engine ソースは `0055c6fce2fe16770f5908ef14cfd7f8a42c2fab`。
`ENGINE_HEADERS` は `InstrumentationEngine.h` のあるディレクトリ、`JSON_HEADERS` は
`nlohmann/json.hpp` を含むディレクトリとする。

```bash
bash poc/scenarios/PlanDrivenInjection/Native/build.sh \
  "$ENGINE_SOURCE" "$ENGINE_HEADERS" "$JSON_HEADERS" /absolute/profiler
```

同じ出力先へ、使用する `libInstrumentationEngine.so` と、ビルドした
`DotNetFaultInjectionAgent.Runtime.deps.json` を配置する。検証では Microsoft.CodeCoverage
18.7.0 の Linux x64 Engine を使用した。テスト用コンテナは
`Native/Dockerfile` から `dotnet-fault-injection-runtime:local` としてビルドできる。

## 計画を作る

実行予定の DLL と対応する Portable PDB、PDB に記録された内容と一致するソースを用意する。
`line:member-name` は同じ行に複数の外部呼び出しがある場合の絞り込みに使う。

`list` の出力には対象のメソッド、IL 位置、呼び出し先のトークンと名前が含まれる。

```powershell
dotnet run --project poc/tools/SourceMap -c Release -- v3 list target.dll Program.cs 22
dotnet run --project poc/tools/SourceMap -c Release -- v3 plan target.dll Program.cs 22 plan.json
dotnet run --project poc/tools/SourceMap -c Release -- v3 verify target.dll Program.cs 22 plan.json
```

生成した計画では次の値を変更する。

- `ruleId`
- 配置先を示す `target.path`
- 発火条件を示す `conditions`
- 障害を示す `fault`
- 発火上限を示す `maxFirings`

ハッシュ・MVID・`site` は生成値を維持し、変更後も `verify` を実行する。

実行先が異なる OS なら `target.path` をその OS の絶対パスへ設定する。
`verify` は引数の DLL を照合し、実行時にはネイティブ拡張が配置先の DLL を照合する。

たとえば生成した計画の条件と障害を次の値に設定できる。

```json
{
  "conditions": {
    "environment": { "CHAOS_TEST_CASE": "enabled" },
    "startAtInvocation": 3
  },
  "fault": {
    "kind": "throw",
    "exceptionType": "System.IO.IOException",
    "message": "planned file failure",
    "delayMs": 0
  },
  "maxFirings": 1
}
```

この JSON は計画の一部を示す例で、全体の形式は `plan.schema.json` にある。
値は `verify` と実行時のパーサーで最終検証する。

| 項目 | 上限 |
|---|---|
| 計画のサイズ | 64 KiB |
| 計画の深さ | 16 |
| 環境条件の値 | 256 UTF-8バイト |
| 例外メッセージ | 1,024 UTF-8バイト |
| 対象 DLL のサイズ（ネイティブ側の検証） | 16 MiB |

## 対象を起動する

対象アプリの配置先へ `DotNetFaultInjectionAgent.Runtime.dll` を追加する。子プロセスの
`DOTNET_ADDITIONAL_DEPS` にはビルドした `DotNetFaultInjectionAgent.Runtime.deps.json` を指定する。
対象アプリへの ProjectReference の追加や、対象 DLL の再ビルドは不要。

対象、Engine、拡張、共通ランタイム、計画は、信頼できる読み取り専用の配置から使う。

```bash
CORECLR_ENABLE_PROFILING=1 \
CORECLR_PROFILER='{324F817A-7420-4E6D-B3C1-143FBED6D855}' \
CORECLR_PROFILER_PATH=/absolute/profiler/libInstrumentationEngine.so \
MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent=/absolute/profiler/Rewrite.xml \
MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1 \
DOTNET_ADDITIONAL_DEPS=/absolute/profiler/DotNetFaultInjectionAgent.Runtime.deps.json \
DOTNET_FAULT_INJECTION_PLAN=/absolute/plan.json CHAOS_TEST_CASE=enabled \
dotnet /absolute/app/target.dll
```

署名検証の設定は専用検証プロセスに限定する。計画はプロセス内で固定され、
変更を反映するには新しいプロセスを起動する。

ネイティブ側は、一度読み取った計画を解析してハッシュを固定し、そのハッシュを挿入する。
共通ランタイムの初回読み取りがその計画と異なる場合は、構成エラーとして停止する。

注入の経過は次の三段階を分けて確認する。

| ログ | 段階 |
|---|---|
| `plan accepted` | 計画の受理 |
| `IL rewritten` | IL の書換え |
| JSON の `chaos.fired` | 障害の発火 |

計画が拒否された場合は、注入を有効化せず元の呼び出しを通す。
対象アプリの成功だけで注入成功と判定せず、三段階とアプリ・外部システムの結果を照合する。

## 独立した対象で検証する

`Probe` は File.WriteAllText を呼ぶ独立した対象で、共通ランタイムへの参照を持たない。
検証スクリプトがランタイム DLL を配置し、additional-deps で読み込ませる。
先にネイティブ拡張と Engine を `Native/bin` へ配置する。

```powershell
python poc/scenarios/PlanDrivenInjection/Tests/verify.py dotnet-fault-injection-runtime:local
```

条件不一致、上限0、12並列呼び出しで上限1、開始回数と上限2、例外型と文言の切替、
遅延を検証する。別の呼び出し箇所への影響、ハッシュ・MVID・メンバー名の不一致、
未対応障害、未知・不足・重複キー、変更されたソースの拒否も確認する。

証跡は `artifacts/verify-<id>/` に保存する。
