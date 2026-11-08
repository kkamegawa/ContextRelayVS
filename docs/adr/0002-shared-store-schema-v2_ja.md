# ADR 0002: Embedded の検索元のために共有ストアを schema バージョン 2 に上げる

[English](0002-shared-store-schema-v2.md)

## 記録のメタデータ

- 状態: 承認
- 担当者: 不明
- 作成日: 2026-10-25
- 最終更新日: 2026-10-25
- 文書の承認: 承認済み（2026-10-25、#224 の設計ゲートでユーザーが承認）
- 追跡: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223)、[Issue #225](https://github.com/kkamegawa/ContextRelayVS/issues/225)
- 関連記録: [共有セッションのスキーマ](../shared-session-schema.md)、[ADR 0001](0001-sharepoint-embedded-retrieval_ja.md)

## 背景と要件

- ピン留めした Embedded のスニペットには、新しい `source` 値が必要である。スキーマ文書は、`source` は閉集合であり、新しい検索元には `schemaVersion` の引き上げが必要だと定めている。
- 現在の実装（確認済み）:
  - `SharedStoreOptions.CurrentSchemaVersion` は 1 で、ストアの全ファイルで共通である。
  - `FileSystemSharedSessionStore` は、`schemaVersion` が異なるファイルを隔離して初期化する。
  - `SharedSnippetRepository` は既に `onenote`、`planner`、`todo` を書き込んでいる。これらはコミット `bbc2223` でバージョンを上げずに追加され、スキーマ文書には今も 6 つの値しか載っていない。
  - ツールウィンドウは保存値を `Enum.TryParse<ContextSource>` で解析する。ハイフンを含む値は解析できず、何も知らせずに既定のメンバーになってしまう。
- VS Code 版はスニペットを `workspaceState` に保存しており、共有ストアはまだ使っていない（S1）。そのため、現時点ではバージョンを上げても影響しない。
- プロジェクトは初期開発段階にある。後方互換とデータ移行は必要ない。
- 2026-10-25 に、バージョン 1 のままならリセットが起きないことをユーザーに伝えたうえで、ユーザーはバージョンの引き上げを選んだ。

## 代替案

| 案 | 要件への適合 | 利点 | コスト・リスク | 根拠 |
|--------|-----------------|----------|---------------|----------|
| 移行なしでバージョン 2 に上げる | 完全 | 文書の規則に従う。古い読み取り側が新しい値を誤読しない | ピン留め、チャット履歴、ハンドオフのインデックスが一度リセットされる | スキーマ §4、§12 |
| バージョン 1 のまま規則を緩める | 部分的 | リセットされない | 文書の契約を再び破る。古い読み取り側が値を誤った検索元として表示する | `bbc2223` |
| 移行処理付きで上げる | 完全 | 既存のデータを残せる | 初期開発段階では不要な移行作業 | ユーザーの指示 |

## 現在の決定

- `source` の集合に `sharepoint-embedded` を加え、`schemaVersion` を 2 に上げる。
- 文書の `source` の集合を、既に書き込まれている値に訂正する: `mail`、`teams`、`sharepoint`、`onedrive`、`connectors`、`onenote`、`planner`、`todo`、`sharepoint-embedded`、`chat`。
- 移行は提供しない。バージョン 1 で書かれたストアのファイルは、既存の破損処理によって一度隔離・初期化される。
- Embedded のスニペットの metadata には、`sourceName`、`containerTypeId`、`extracts`（`{text, relevanceScore?}` の配列）、`sensitivityLabelDisplayName`、`sensitivityLabelId` を使う。最初の 2 つは upstream と同じ名前にする。
- 読み取り側は、列挙型の名前の解析に頼らず、`sharepoint-embedded` を Embedded の検索元へ明示的に対応付ける。

## 影響と見直しの条件

- ユーザーは、既存のピン留め、チャット履歴、ハンドオフの参照を一度失う。リリースノートにこれを明記する。
- リリース済みのバージョン 1 の拡張機能とバージョン 2 のビルドは、互いのファイルを隔離し合う。設定と共有ストアは Experimental Instance と通常の Visual Studio で共有されるため、検証ガイドでテスト担当者にリリース版の拡張機能を無効にするよう案内する。
- VS Code 版が共有ストアを採用するときは、この検索元の集合とバージョン 2 から始める。
- ユーザーのデータを残す必要がある公開リリースの前に見直す。その後のバージョン引き上げには移行処理が必要になる。

## 出典

| ID | タイトル | URL | 確認日 | 該当する記述・節 | この決定との関係 |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | ContextRelay `src/snippets/snippetStore.ts` | https://github.com/kkamegawa/ContextRelay/blob/main/src/snippets/snippetStore.ts | 2026-10-25 | スニペットは `workspaceState` に保存される | バージョンを上げても VS Code 版に影響しない |

## 変更履歴

この記録は、置き換え用の ADR を追加する方式ではなく、承認を得てその場で更新する方式を使う。履歴の行はすべて残す。

| 日付 | 以前の決定の要約 | 新しい決定の要約 | 理由 | 出典 ID | 承認の参照 |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | 初回の提案 | `sharepoint-embedded` を含む schema バージョン 2。移行なし | 閉集合への新しい検索元の追加。ユーザーがバージョンの引き上げを再確認した | S1 | ユーザー承認、2026-10-25（#224） |
