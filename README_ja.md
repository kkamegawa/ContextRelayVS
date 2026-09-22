# ContextRelay for Visual Studio (日本語)

ContextRelay for Visual Studio は、Visual Studio (2022 / 2026) 上で Microsoft 365 (Exchange Mail、Microsoft Teams、SharePoint、OneDrive) のコンテキストをツールウィンドウに表示する拡張機能です。VS Code 版 [ContextRelay](https://github.com/kkamegawa/ContextRelay) の機能仕様をそのまま Visual Studio に移植し、同一マシン上の VS Code 版とスニペット・チャット履歴・ハンドオフ文書パスを共有できます。

> **現状**: 実装済みプレビューです。ローカルでインストール可能な VSIX をビルドでき、ローカライズ済みツールウィンドウ、スラッシュコマンド補完、結果アクション、`/connectors`、`/ask`、`/workiq` まで含む計画済みのリポジトリ内機能を備えています。Marketplace 公開前に Experimental Instance での手動検証が必要です。

## 実装済み機能

- 通常の Copilot チャット — スラッシュコマンドなしの入力で Microsoft 365 Copilot の会話を開始・継続する。個々の検索結果は添付せず、明示的コンテキストはピン留めスニペット、保留中の添付ファイル、`#file` メンション、有効化した場合の保存済みアクティブ エディター。直近の ContextRelay 検索要約は参考情報として送信される場合があるが、明示的コンテキストには含めない。`/ask` と異なり、明示的コンテキストがなくても実行できる
- Exchange Mail / Teams / SharePoint / OneDrive を対象とするキーワード検索 (Microsoft Graph 経由)
- スラッシュコマンドによるソース指定 — `/mail` `/teams` `/sharepoint` `/onedrive` `/connectors` `/all` `/ask` `/workiq` `/clear`
- `/` 入力時に表示されるキーボード操作対応のスラッシュコマンド候補ポップアップ
- 検索結果を名前付きで保存するスニペットピン (クロスエディタ共有ストアに永続化)
- VS Code 版と共有されるチャット/検索履歴
- タイムスタンプ付きハンドオフ文書 (`PLAN.md` / `TASKS.md` / `TEST_PLAN.md` / 任意で `HANDOFF.md`) の生成
- Copilot for Visual Studio へ渡すためのソフトハンドオフ (プロンプトのクリップボード転送、選択結果の `HANDOFF.md` 追記、利用可能な場合の GitHub Copilot Chat 自動オープン)
- `/ask` では [Issue #184](https://github.com/kkamegawa/ContextRelayVS/issues/184) に定めた明示的コンテキストと添付の規則を適用し、ピン留めスニペット、保留中のローカル添付、有効化した保存済みアクティブ エディターを上限内で送信する。利用可能なコンテキストがない場合は実行しない
- `/workiq` では Work IQ Gateway に A2A v1.0 で自然言語クエリを送り、専用トークン audience と会話 `contextId` を使って Microsoft 365 のワークインテリジェンスを問い合わせ
- 英語/日本語の UI 文言、結果カードのコンテキストアクション、ステータス/ヘルプ文言を備えた WPF ツールウィンドウ UI
- General / Chat / Authentication / Cache / Adapters の Options ページ。Chat には添付ファイル上限 (既定値 5、0 で無効)、アクティブ エディター自動添付 (既定値オフ)、応答ストリーミング (既定値オン) を含む
- ファイル メンションの **添付ファイルの最大数** (既定値 5) は通常のチャットと `/ask` に適用し、`/workiq` は常に最大 5 件の重複しない `#file` メンションを受け付ける。不完全な可能性がある応答にはその旨が表示され、**続きを取得** ボタンで手動継続できる
- MSAL.NET + WAM 認証と DPAPI ベースのトークンキャッシュ
- TTL + LRU キャッシュとワークスペース永続化
- VS Code 版との **クロスエディタセッション共有** — `%LocalAppData%\ContextRelay\shared\` を介してスニペット/チャット履歴/ハンドオフ文書インデックスを同期。詳細は [docs/shared-session-schema.md](docs/shared-session-schema.md)

## チャットのコンテキスト・添付・ストリーミング

通常のチャットと `/ask` は同じ明示的コンテキスト規則を使います。`/ask` は利用できる明示的コンテキストがない場合、認証と Copilot 要求の前にローカルで拒否しますが、通常のチャットはいずれの場合でも実行されます。直近の ContextRelay 検索要約がある場合は参考情報として送信しますが、明示的コンテキストではなく、`/ask` の判定も Web コンテキストの扱いも変えません。

- **添付** — ツールウィンドウの **+** ボタン、または Tools > ContextRelay メニューの **チャットにファイルを添付** でワークスペース内のファイルを保留し、チップの **削除** で外せます。`#file` メンション、保留中の添付、アクティブ エディターの順で **添付ファイルの最大数** (Maximum attached files) の上限まで添付し、`0` にすると添付を無効化します。送信した添付はその要求が占有して保留キューから外すため、応答生成中に追加したファイルは次の要求用に残ります。
- **アクティブ エディター** — **アクティブなエディターを添付** (Attach active editor) を有効にすると、アクティブ エディターの保存済み内容を添付します。選択範囲があればその行だけを送りますが、未保存の編集がある場合はディスク上の行番号と一致しないため、選択を無視して保存済みファイル全体を添付します。
- **グラウンディング** — 明示的コンテキストを含む要求には、添付ファイルとピン留めスニペットを主な情報源として扱う指示を付加し、その要求では Copilot の Web コンテキストを無効化します。検索要約だけの要求はそのまま送信します。
- **ストリーミング** — **チャット応答をストリーミング** (Stream chat responses) が有効なら応答を受信中に逐次表示します。応答の生成中はコンポーザーの主ボタンが **停止** に切り替わり、そこから生成をキャンセルできます。ボタンを入れ替えないためキーボード フォーカスを維持します。停止後に自動で継続要求を送ることはありません。

## ビルドとパッケージング

- Visual Studio 2022 17.8 以降、または Visual Studio 2026 (Insider 含む)
- .NET Framework 4.8 ランタイム (Visual Studio に同梱)
- `dotnet test` を実行するマシンには .NET 10 SDK 以降が必要 (リポジトリの `global.json` が Microsoft.Testing.Platform ランナーを選択しているため)
- Microsoft 365 職場/学校アカウント (Microsoft Entra ID)。個人用 Microsoft アカウントは非対応
- パブリック クライアント フローを有効化した Microsoft Entra アプリ登録、Microsoft Graph の委任アクセス許可、および `/workiq` 用の `WorkIQAgent.Ask` (任意)。設定手順は [docs/tenant_admin_quickstart.md](docs/tenant_admin_quickstart.md) を参照してください。

```powershell
pwsh -File build\Invoke-PackageAudit.ps1 -SolutionPath .\ContextRelayVS.sln
dotnet build ContextRelayVS.sln -v minimal
dotnet test tests\ContextRelay.Core.Tests\ContextRelay.Core.Tests.csproj -v minimal
```

生成される VSIX:

```text
src\ContextRelay.VSExtension\bin\<Configuration>\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix
```

## 手動検証

- [docs/e2e_checklist.md](docs/e2e_checklist.md) の手順を使用してください。
- 公開準備時は [docs/marketplace_release.md](docs/marketplace_release.md) の Marketplace / release 手順も参照してください。
- 公開前に Visual Studio Experimental Instance (`/rootsuffix Exp`) で読み込み確認を行ってください。

## アーキテクチャ

| レイヤ | プロジェクト | フレームワーク |
|---|---|---|
| VSIX / ツールウィンドウ / コマンド / オプション | `src/ContextRelay.VSExtension` | net8.0-windows10.0.22621.0 |
| ビジネスロジック (アダプタ / ルータ / キャッシュ / スニペット / ハンドオフ / 共有ストア / 認証) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF ビュー & ビューモデル (MVVM) | `src/ContextRelay.UI` | net8.0-windows |
| 単体テスト | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

認証には **MSAL.NET** (`Microsoft.Identity.Client`) と Windows Account Manager (WAM) ブローカーを使用。トークンは DPAPI 暗号化された `MsalCacheHelper` でキャッシュします。

UI は WPF で実装し、`VsBrushes` / `EnvironmentColors` にバインドすることで VS のテーマ (ダーク / ライト / Blue) に自動追従します。

## 現在の既知の未完了事項

- Experimental Instance 上のホスト実行確認は手動検証が残っています。
- Marketplace 公開には PAT の投入と手動 release 実行が必要です。
- VS Code 側リポジトリの shared-store migration PR は別作業です。
- GitHub Copilot Chat へのプロンプト直接注入は、VS Code 版と異なり Visual Studio 側で利用可能なサポート済み API がないため、現状はクリップボード経由です。

## Work IQ

`/workiq` は Work IQ Gateway に対して A2A (Agent-to-Agent) v1.0 プロトコルで自然言語クエリを送信します。

- エンドポイント: `https://workiq.svc.cloud.microsoft/a2a/`
- 委任アクセス許可: `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`
- 前提条件: Microsoft 365 Copilot ライセンス、テナント管理者の同意、Work IQ サービス プリンシパルのプロビジョニング

例:

```text
/workiq Alice からの最近のメールを要約して
/workiq 今日の会議を教えて
/workiq Q3 予算レビューに関する文書を探して
```

連続する `/workiq` クエリは Work IQ の `contextId` を再利用します。`/clear` を実行すると Microsoft 365 Copilot と Work IQ の両方の会話状態がリセットされます。詳細は [docs/work_iq_ja.md](docs/work_iq_ja.md) を参照してください。

## ライセンス

MIT。[LICENSE](LICENSE) を参照してください。

## 関連

- 上流 VS Code 拡張: <https://github.com/kkamegawa/ContextRelay>
- 設計プラン: [docs/plan.md](docs/plan.md)
- Marketplace / release ガイド: [docs/marketplace_release.md](docs/marketplace_release.md)
- テナント管理者向けクイックスタート: [docs/tenant_admin_quickstart.md](docs/tenant_admin_quickstart.md)
- Work IQ 設定: [docs/work_iq_ja.md](docs/work_iq_ja.md)
- 共有セッションスキーマ: [docs/shared-session-schema.md](docs/shared-session-schema.md)
