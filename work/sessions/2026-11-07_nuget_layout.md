# 2026-11-07: NuGet公開に向けたフォルダ整理

## 変更

- 配布実装をsrc/Tool、Planner、Runtime、Shared、Native/Windows、Native/Linuxへ移動。
- 検証対象・ホスト・自動試験をtests/Fixtures、OrderAgentProbe、CoreFileTarget、PlanDrivenInjectionへ移動。
- version 3スキーマをschemas/へ、Windows起動スクリプトをtools/へ移動。
- 生成パッケージとパッケージ検証出力をartifacts/へ集約し、Git管理から除外。
- 管理対象をビルドするslnx、Toolだけをpack対象にする設定、Pack.ps1、Test-Package.ps1を追加。
- NuGet用README・スキーマをパッケージに追加し、メタデータ・プロジェクト参照・文書リンク・実行スクリプトを更新。
- 既存の過去セッション記録は当時の事実として維持。

## 検証

- 新構成のソリューションビルド: エラー・警告0。
- Pack.ps1: README欠落警告なしで成功。
- 追跡ファイルだけを別ディレクトリへ展開した構成から再packし、新規ローカルフィード・空キャッシュでインストール成功。capabilities、実Probeの計画生成・照合が成功。[パッケージ証跡](2026-11-07_nuget-layout/package-summary.json)
- 指定した新パッケージを用いて、注文投入を含む全サンプル準備が成功。
- Windowsサンプル7種類成功。[証跡](2026-11-07_nuget-layout/windows-summary.json)
- Linuxネイティブを新配置から再ビルドし、既存17ケース＋旧DLL同居1ケースの計18ケース成功。[証跡](2026-11-07_nuget-layout/linux-summary.json)
- PowerShell構文、Markdown相対リンク、csprojの共有ソース・プロジェクト参照、差分チェックに成功。

Windowsネイティブは直前の改名検証でビルド・実行確認した同一ソースのバイナリを新配置に置いて使用。Windowsサービスの実動作は今回再実行しておらず、直前の改名検証の結果と区別する。

## 公開前の残件

NuGet.orgへは公開していない。ライセンス未指定、公開バージョン・パッケージID権限・認証の確認、非公開GitHubに代わる利用者向け文書の閲覧先が残る。Native/RuntimeをNuGetで配る設定は今回追加していない。
