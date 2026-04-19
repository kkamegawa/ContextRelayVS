# ContextRelay for Visual Studio (日本語)

ContextRelay for Visual Studio は、Visual Studio (2022 / 2026) 上で Microsoft 365 (Exchange Mail、Microsoft Teams、SharePoint、OneDrive) のコンテキストをツールウィンドウに表示する拡張機能です。VS Code 版 [ContextRelay](https://github.com/kkamegawa/ContextRelay) の機能仕様をそのまま Visual Studio に移植し、同一マシン上の VS Code 版とスニペット・チャット履歴・ハンドオフ文書パスを共有できます。

> **現状**: 設計 / 骨組みフェーズです。実装ロードマップを含む設計書は [docs/plan.md](docs/plan.md) を参照してください。VSIX はまだ配布していません。

## 機能 (予定、VS Code 版と同一仕様)

- Exchange Mail / Teams / SharePoint / OneDrive を対象とするキーワード検索 (Microsoft Graph 経由)
- スラッシュコマンドによるソース指定 — `/mail` `/teams` `/sharepoint` `/onedrive` `/all` `/ask` `/clear`
- 検索結果を名前付きで保存するスニペットピン
- タイムスタンプ付きハンドオフ文書 (`PLAN.md` / `TASKS.md` / `TEST_PLAN.md` / 任意で `HANDOFF.md`) の生成
- VS Code 版との **クロスエディタセッション共有** — `%LocalAppData%\ContextRelay\shared\` を介してスニペット/チャット履歴/ハンドオフ文書インデックスを同期。詳細は [docs/shared-session-schema.md](docs/shared-session-schema.md)

## 要件 (予定)

- Visual Studio 2022 17.8 以降、または Visual Studio 2026 (Insider 含む)
- .NET Framework 4.7.2 ランタイム (Visual Studio に同梱)
- Microsoft 365 職場/学校アカウント (Microsoft Entra ID)。個人用 Microsoft アカウントは非対応
- パブリック クライアント フローを有効化した Microsoft Entra アプリ登録と、Microsoft Graph の委任アクセス許可 (VS Code 版と同一スコープ)

## アーキテクチャ

| レイヤ | プロジェクト | フレームワーク |
|---|---|---|
| VSIX / ツールウィンドウ / コマンド / オプション | `src/ContextRelay.VSExtension` | .NET Framework 4.7.2 |
| ビジネスロジック (アダプタ / ルータ / キャッシュ / スニペット / ハンドオフ / 共有ストア / 認証) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF ビュー & ビューモデル (MVVM) | `src/ContextRelay.UI` | .NET Framework 4.7.2 |
| 単体テスト | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

認証には **MSAL.NET** (`Microsoft.Identity.Client`) と Windows Account Manager (WAM) ブローカーを使用。トークンは DPAPI 暗号化された `MsalCacheHelper` でキャッシュします。

UI は WPF で実装し、`VsBrushes` / `EnvironmentColors` にバインドすることで VS のテーマ (ダーク / ライト / Blue) に自動追従します。

## ライセンス

MIT。[LICENSE](LICENSE) を参照してください。

## 関連

- 上流 VS Code 拡張: <https://github.com/kkamegawa/ContextRelay>
- 設計プラン: [docs/plan.md](docs/plan.md)
- 共有セッションスキーマ: [docs/shared-session-schema.md](docs/shared-session-schema.md)
