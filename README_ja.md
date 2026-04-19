# ContextRelay for Visual Studio (日本語)

ContextRelay for Visual Studio は、Visual Studio (2022 / 2026) 上で Microsoft 365 (Exchange Mail、Microsoft Teams、SharePoint、OneDrive) のコンテキストをツールウィンドウに表示する拡張機能です。VS Code 版 [ContextRelay](https://github.com/kkamegawa/ContextRelay) の機能仕様をそのまま Visual Studio に移植し、同一マシン上の VS Code 版とスニペット・チャット履歴・ハンドオフ文書パスを共有できます。

> **現状**: 実装済みプレビューです。ローカルでインストール可能な VSIX をビルドでき、コア機能・共有ストア・CI パッケージングまで揃っています。Marketplace 公開前に Experimental Instance での手動検証が必要です。

## 実装済み機能

- Exchange Mail / Teams / SharePoint / OneDrive を対象とするキーワード検索 (Microsoft Graph 経由)
- スラッシュコマンドによるソース指定 — `/mail` `/teams` `/sharepoint` `/onedrive` `/all` `/ask` `/clear`
- 検索結果を名前付きで保存するスニペットピン (クロスエディタ共有ストアに永続化)
- VS Code 版と共有されるチャット/検索履歴
- タイムスタンプ付きハンドオフ文書 (`PLAN.md` / `TASKS.md` / `TEST_PLAN.md` / 任意で `HANDOFF.md`) の生成
- Copilot for Visual Studio へ渡すためのソフトハンドオフ (プロンプトのクリップボード転送)
- 検索結果 / スニペット / 履歴 / アクションを含む WPF ツールウィンドウ UI
- General / Authentication / Cache / Adapters の Options ページ
- MSAL.NET + WAM 認証と DPAPI ベースのトークンキャッシュ
- TTL + LRU キャッシュとワークスペース永続化
- VS Code 版との **クロスエディタセッション共有** — `%LocalAppData%\ContextRelay\shared\` を介してスニペット/チャット履歴/ハンドオフ文書インデックスを同期。詳細は [docs/shared-session-schema.md](docs/shared-session-schema.md)

## ビルドとパッケージング

- Visual Studio 2022 17.8 以降、または Visual Studio 2026 (Insider 含む)
- .NET Framework 4.7.2 ランタイム (Visual Studio に同梱)
- Microsoft 365 職場/学校アカウント (Microsoft Entra ID)。個人用 Microsoft アカウントは非対応
- パブリック クライアント フローを有効化した Microsoft Entra アプリ登録と、Microsoft Graph の委任アクセス許可 (VS Code 版と同一スコープ)

```powershell
dotnet build ContextRelayVS.sln -v minimal
dotnet test tests\ContextRelay.Core.Tests\ContextRelay.Core.Tests.csproj -v minimal
```

生成される VSIX:

```text
src\ContextRelay.VSExtension\bin\<Configuration>\net472\ContextRelay.VSExtension.vsix
```

## 手動検証

- [docs/e2e_checklist.md](docs/e2e_checklist.md) の手順を使用してください。
- 公開前に Visual Studio Experimental Instance (`/rootsuffix Exp`) で読み込み確認を行ってください。

## アーキテクチャ

| レイヤ | プロジェクト | フレームワーク |
|---|---|---|
| VSIX / ツールウィンドウ / コマンド / オプション | `src/ContextRelay.VSExtension` | .NET Framework 4.7.2 |
| ビジネスロジック (アダプタ / ルータ / キャッシュ / スニペット / ハンドオフ / 共有ストア / 認証) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF ビュー & ビューモデル (MVVM) | `src/ContextRelay.UI` | .NET Framework 4.7.2 |
| 単体テスト | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

認証には **MSAL.NET** (`Microsoft.Identity.Client`) と Windows Account Manager (WAM) ブローカーを使用。トークンは DPAPI 暗号化された `MsalCacheHelper` でキャッシュします。

UI は WPF で実装し、`VsBrushes` / `EnvironmentColors` にバインドすることで VS のテーマ (ダーク / ライト / Blue) に自動追従します。

## 現在の既知の未完了事項

- Marketplace 公開フロー自体は未接続で、CI は artifact 生成までです。
- Experimental Instance 上のホスト実行確認は手動検証が残っています。
- VS Code 側リポジトリの shared-store migration PR は別作業です。

## ライセンス

MIT。[LICENSE](LICENSE) を参照してください。

## 関連

- 上流 VS Code 拡張: <https://github.com/kkamegawa/ContextRelay>
- 設計プラン: [docs/plan.md](docs/plan.md)
- 共有セッションスキーマ: [docs/shared-session-schema.md](docs/shared-session-schema.md)
