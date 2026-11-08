# ADR 0004: 共有設定を「読み込み→変更→保存」で更新し、Embedded の Options ページを追加する

[English](0004-shared-settings-updates-and-embedded-options.md)

## 記録のメタデータ

- 状態: 承認
- 担当者: 不明
- 作成日: 2026-10-25
- 最終更新日: 2026-10-25
- 文書の承認: 承認済み（2026-10-25、#224 の設計ゲートでユーザーが承認）
- 追跡: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223)、[Issue #227](https://github.com/kkamegawa/ContextRelayVS/issues/227)
- 関連記録: [設計計画 §8](../plan.md#8-options-dialogpage)、[ADR 0001](0001-sharepoint-embedded-retrieval_ja.md)

## 背景と要件

- 現在の実装（確認済み）:
  - `ContextRelayOptionsPage.CreateSettingsSnapshot()` は、ページの内容から設定全体のスナップショットを作り、`%AppData%\ContextRelay\settings.json` に上書き保存する。
  - プロセス外のホストも、UI 言語の変更時などにこのファイルへ書き込む。
  - `ContextRelaySettingsStore` は `File.Replace` でファイルを原子的に置き換える。書き込み途中の破損は防げるが、プロセス間での更新の消失は防げない。
  - ビルドタスク `PatchPkgdefFile` は、すべての `@="#1"` を `General` に置き換え、`IsInUnifiedSettings` を 0 にする。そのため 1 ページにしか対応していない。`build/EnableUnifiedSettings.ps1` は `ContextRelay\General` を直書きしている。
  - net48 のパッケージには `UseWPF` がなく、General ページは英語の `DisplayName` と `Category` 属性を直書きしている。
- Embedded の検索元は項目ごとの検証が必要なレコードの一覧であり、プロパティグリッドでは編集しにくい。
- `UIElementDialogPage` は、キーボード操作に対応した WPF のコンテンツを Options ページに表示できる（S1）。`DialogPage.OnApply` では、ページが Apply を取り消せる（S2）。

## 代替案

| 案 | 要件への適合 | 利点 | コスト・リスク | 根拠 |
|--------|-----------------|----------|---------------|----------|
| 名前付きミューテックスで保護した「読み込み→変更→保存」と WPF の Embedded ページ | 完全 | 更新が消失しない。一覧の編集と検証が使いやすい | net48 のパッケージに WPF が加わる | S1、S2 |
| 全体スナップショットの保存のまま、Embedded をプロパティグリッドに加える | 低い | 変更が最小 | 1 回の OK で 2 ページが互いを上書きする。一覧の編集が使いにくい | 現在のコード |
| Unified Settings に登録する | 部分的 | 新しい設定 UI | この拡張機能はまだ採用しておらず、移行が大きい | 現在の pkgdef パッチ |

## 現在の決定

- `ContextRelaySettingsStore` に、最新のファイルを読み込み、呼び出し側の変更だけを適用して原子的に保存する更新 API を追加する。読み込みから保存までの全体を、プロセス間の名前付きミューテックスで保護する。書き込む側はすべてこの API を使う。対象は devenv 内の 2 つの Options ページと、プロセス外のホストである。
- General ページと Embedded ページは、それぞれ自分の担当フィールドだけを変更する。全体スナップショットでの保存は廃止する。
- Tools > Options > ContextRelay に SharePoint Embedded ページを追加する。net48 のパッケージに `UseWPF` を加え、`UIElementDialogPage` で表示する。ページには、有効化チェックボックス、前提条件の説明（preview、従量課金、Global のみ、所有アプリによる権限付与）、追加・編集・削除ができる検索元の一覧、項目ごとの検証、クラウドが Global 以外のときの警告を置く。
- 検証に失敗したら Apply を取り消し、エラーを表示する。Cancel では編集内容を破棄し、保存済みの値を再読み込みする。
- Options と実行時で 1 つの検証器を共有する。名前は trim 後に 1〜64 文字で、大文字小文字を区別せず重複不可とする。container type ID は GUID とし、小文字の `D` 形式に正規化して重複不可とする。
- `PatchPkgdefFile` をページのキーごとに表示名を設定する方式に変え、`EnableUnifiedSettings.ps1` を汎用化して、両方のページで `IsInUnifiedSettings=0` を維持する。
- General ページは `Localized*` 属性に移行して英日のリソースを用意し、「全般、チャット、認証、キャッシュ、検索対象、診断、プライバシー」に分類する。

## 影響と見直しの条件

- devenv とホストが同時に保存しても、互いの変更は失われなくなる。
- `InProcPackageVsixPackagingTests` で、ビルドした VSIX の両ページの登録と表示名を検証する必要がある。
- 全体スナップショットで書き込むリリース版の拡張機能は Embedded の設定を消しうるため、検証はリリース版の拡張機能を無効にして行う。
- 拡張機能が Unified Settings を採用するときに見直す。

## 出典

| ID | タイトル | URL | 確認日 | 該当する記述・節 | この決定との関係 |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | UIElementDialogPage Class | https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.uielementdialogpage | 2026-10-25 | タブ移動とキーボード操作に対応して、WPF のコンテンツをダイアログ ページに表示する | Embedded ページの表示方式 |
| S2 | DialogPage.OnApply Method | https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.dialogpage.onapply | 2026-10-25 | Apply を処理する。オーバーライドした実装が保存を制御する | 検証付きの Apply |

## 変更履歴

この記録は、置き換え用の ADR を追加する方式ではなく、承認を得てその場で更新する方式を使う。履歴の行はすべて残す。

| 日付 | 以前の決定の要約 | 新しい決定の要約 | 理由 | 出典 ID | 承認の参照 |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | 初回の提案 | 「読み込み→変更→保存」による設定の更新と WPF の Embedded Options ページ | 2 つのページとホストの間での更新の消失。Embedded の検索元の一覧の編集 | S1、S2 | ユーザー承認、2026-10-25（#224） |
