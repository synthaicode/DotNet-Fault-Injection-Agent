# 注入計画を作るツール

対象バイナリと、対応するソース・Portable PDBを照合して、注入用JSONを作るツールです。
利用者は注入箇所を選び、発火条件、障害、回数を指定します。
Windowsの専用試験では version 1/2、汎用のCoreCLR試験では version 3 の計画を使います。
[サンプル一覧](../../samples/README.md)で、それぞれの対応範囲を説明しています。
コマンド、プロジェクト解析の制約、計画形式の詳細は [開発用の説明](DEVELOPMENT.md) にあります。

パッケージ名は `DotNetFaultInjectionAgent.Tool`、コマンド名は `dotnet-fault-injection` です。
ローカルパッケージの作成・インストールは[サンプルの準備](../../samples/OPERATIONS.md#配布物の準備)を参照してください。
version 1/2では `dotnet-fault-injection list <source|project> <binary> <references>`、
`plan <source|project> <binary> <references> <selection> <plan.json>`、
`verify <source|project> <binary> <references> <plan.json>` を使います。
version 3では `dotnet-fault-injection v3 capabilities` などの入口を使います。
