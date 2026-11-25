# ADR 0001: 設定済みの SharePoint Embedded コンテンツを専用トークンで取得する

[English](0001-sharepoint-embedded-retrieval.md)

## 記録のメタデータ

- 状態: 承認
- 担当者: 不明
- 作成日: 2026-10-25
- 最終更新日: 2026-10-25
- 文書の承認: 承認済み（2026-10-25、#224 の設計ゲートでユーザーが承認）
- 追跡: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223)、[Issue #225](https://github.com/kkamegawa/ContextRelayVS/issues/225)
- 関連記録: [設計計画 §6.1](../plan.md#61-sharepoint-embedded-retrieval)、[ADR 0002](0002-shared-store-schema-v2_ja.md)、[ADR 0003](0003-single-column-tool-window-and-markdown_ja.md)、[ADR 0004](0004-shared-settings-updates-and-embedded-options_ja.md)

## 背景と要件

- VS Code 版は [kkamegawa/ContextRelay#287](https://github.com/kkamegawa/ContextRelay/issues/287) で、オプトイン方式の SharePoint Embedded 検索を追加した。Visual Studio 版も同じ動作を提供する必要がある。
- 現在の実装（確認済み）:
  - SharePoint と OneDrive は Graph Search を使う。
  - connectors は `RetrievalSearchAdapter` を通じて Copilot Retrieval API を使う。
  - Graph の検索元はすべて 1 つのトークンを共有し、そのスコープは `AuthScopeCatalog.BuildRequiredScopes` で決まる。
  - Work IQ は既に `MsalAuthProvider.GetWorkIqAccessTokenAsync` で専用のトークンを使っている。
- SharePoint Embedded のコンテンツには、次の層が必要である（S1、S3）。
  - delegated の Graph 権限 `FileStorageContainer.Selected`
  - 所有アプリだけが登録できる container type のアプリ権限付与
  - ユーザー自身のコンテナーへのアクセス権
  - content discoverability、従量課金、初期化済みの semantic index
- Retrieval API 自体は一般提供だが、`sharePointEmbedded` データソースとその課金は preview である（S1、S2）。API は Global クラウドでのみ提供される（S4）。
- upstream は当初 `FileStorageContainer.Selected` を共有セッションに加えた。その結果、Embedded の同意が不足するとすべての検索元のサインインが壊れ、upstream は Embedded を専用セッションに移した（S5）。

## 代替案

| 案 | 要件への適合 | 利点 | コスト・リスク | 根拠 |
|--------|-----------------|----------|---------------|----------|
| Embedded 専用トークンで Retrieval を直接呼ぶ | 完全 | 同意不足の影響は `/embedded` だけ。Work IQ のトークン方式を再利用できる | 最初の `/embedded` で 2 回目の同意が発生する | S1、S5 |
| 共有トークンにスコープを加えて Retrieval を直接呼ぶ | 部分的 | 同意は 1 回 | upstream で起きたとおり、同意不足ですべての検索元が止まる | S5 |
| container type で絞った Graph Search | 部分的 | preview の課金がない | 返るのはヒットで、グラウンディング用の抽出文ではない。upstream と異なる | S1 |
| 読み取り専用の SPE MCP を実行時に使う | 低い | アダプターのコードが不要 | Node.js の実行時依存が増え、製品の境界を越える | S5 |

## 現在の決定

- オプトイン方式の `/embedded <自然文のクエリ>` を追加する。`/all` には含めず、他の検索元へフォールバックしない。
- Embedded が有効になるのは、`EmbeddedEnabled` が true で、`CloudEnvironment` が Global で、有効で妥当な検索元が 1 件以上あるときだけである。各検索元は表示名 `Name`、`ContainerTypeId`、`Enabled` を持つ。
- 有効条件、検索元の選択、trim 後に 1〜1500 文字のクエリであることを、認証の前に検証する。件数は 1〜25 に丸める。
- delegated `FileStorageContainer.Selected` 用の専用トークンを `GetEmbeddedAccessTokenAsync` で取得する。このスコープは `BuildRequiredScopes` に決して加えない。同意の失敗時はスコープ名を示し、所有アプリによる権限付与とアプリ登録を案内する。
- Embedded だけを使う場合は、テナントでの確認で必要と判明しない限り `Files.Read.All` と `Sites.Read.All` を加えない。
- `POST /v1.0/copilot/retrieval` に、`dataSource: sharePointEmbedded`、選択した `containerTypeId`、`resourceMetadata: ["title"]`、`maximumNumberOfResults` を指定して送る。送信と抽出文の変換は connectors と共通化する。
- 400、401、403、404、429 をそれぞれ具体的な案内に対応付ける。0 件は、200 応答で `retrievalHits` が空の場合だけ報告する。
- Embedded 用のキャッシュはメモリのみとし、アカウント、クラウドとエンドポイント、container type、件数、クエリで分離する。ワークスペースのキャッシュ スナップショットには決して書き込まない。
- アカウント、認証、Embedded の設定が変わったら、キャッシュと表示中の Embedded 結果を破棄する。要求ごとの世代番号で古い応答を捨てる。設定変更で無効になった要求は、何も表示せずに消すのではなく、メッセージで知らせる。
- ピン留めした Embedded の抽出文は、既存の予算の範囲でチャットの `additionalContext` に渡し、Ask プロンプトとハンドオフ文書にも含める。Work IQ には決して送らず、ファイル参照にも変換しない。

## 影響と見直しの条件

- ユーザーは、最初に `/embedded` を実行したときに 2 回目の同意を求められる。その同意がなくても、他の検索元は動き続ける。
- preview であることと従量課金を、UI と文書に明示する必要がある。
- 設定は対象を指定するだけで、権限を与えるものではない。Microsoft 所有の Loop コンテナーで動作することは約束しない。
- 確信度は中程度である。要求の形式、権限、クラウドの提供状況は現在の文書に基づくが、実テナントでの動作は承認済みの環境で確認するまで未検証である。
- データソースが一般提供になったとき、Graph Search が抽出文を返せるようになったとき、またはテナントでの確認で追加のスコープが必要と判明したときに見直す。

## 出典

| ID | タイトル | URL | 確認日 | 該当する記述・節 | この決定との関係 |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | Add Microsoft 365 Copilot and agent experiences | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/agent-experiences | 2026-10-25 | Use the Retrieval API: 要求本文、`FileStorageContainer.Selected`、preview、前提条件、課金、discoverability | 要求と前提条件を定める |
| S2 | Billing meters | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/reference/billing-meters | 2026-10-25 | `sharePointEmbedded` データソースとその課金は preview。Retrieval API は GA | preview と課金の案内 |
| S3 | Plan authentication and permissions | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/authentication-permissions | 2026-10-25 | delegated `FileStorageContainer.Selected` は管理者同意不要。container type の権限付与が必要 | トークンと権限付与の設計 |
| S4 | Retrieve grounding data | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/retrieval/copilotroot-retrieval | 2026-10-25 | national cloud の表: Global のみ | 有効条件に Global を含める |
| S5 | ContextRelay ADR 0001 | https://github.com/kkamegawa/ContextRelay/blob/main/docs/adr/0001-sharepoint-embedded-retrieval.md | 2026-10-25 | 変更履歴: 共有セッションのスコープがすべてのアダプターを壊し、Embedded を専用セッションに移した | 専用トークンの理由 |

## 変更履歴

この記録は、置き換え用の ADR を追加する方式ではなく、承認を得てその場で更新する方式を使う。履歴の行はすべて残す。

| 日付 | 以前の決定の要約 | 新しい決定の要約 | 理由 | 出典 ID | 承認の参照 |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | 初回の提案 | 専用トークンによるオプトイン方式の Embedded 直接検索 | upstream #287 の移植。最初に承認された仕様の共有スコープ方式を、専用トークンに置き換えた | S1〜S5 | ユーザー承認、2026-10-25（#224） |
