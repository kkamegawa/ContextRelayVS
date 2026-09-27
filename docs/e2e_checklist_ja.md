# ContextRelay for Visual Studio E2E チェックリスト（日本語）

このチェックリストは、リリース前に Visual Studio Experimental Instance で実行します。英語版の全項目は [e2e_checklist.md](e2e_checklist.md) を参照してください。

## インストールと起動

1. 生成した `ContextRelay.VSExtension.vsix` をインストールします。
2. `/rootsuffix Exp` で Visual Studio を起動します。
3. **拡張機能 > 拡張機能の管理** に拡張機能が表示されることを確認します。
4. **表示 > その他のウィンドウ > ContextRelay** を開き、例外なくツール ウィンドウが表示されることを確認します。

## 自動 UI 言語選択

1. **ツール > オプション > 環境 > 国際設定** で Visual Studio の表示言語を英語にします。Windows と拡張機能プロセスの UI カルチャは日本語のままにし、ContextRelay の **UI language** を `auto` に設定して Visual Studio を再起動します。初期ツール ウィンドウのラベル、状態、ヘルプ、結果ラベル、Options のラベルが英語になることを確認します。
2. Visual Studio の表示言語を日本語にし、Windows と拡張機能プロセスの UI カルチャを英語にした状態で同じ確認を行います。`auto` ではツール ウィンドウと Options のラベルが日本語になることを確認します。
3. Visual Studio の表示言語を未対応の言語にするか、Experimental Instance でホスト ロケールを取得できない状態を再現します。`auto` ではすべての ContextRelay UI が英語へフォールバックし、リソース キーがそのまま表示されないことを確認します。
4. Visual Studio が日本語の状態で `en` を明示的に選択し、ツール ウィンドウと Options が英語になることを確認します。Visual Studio が英語の状態で `ja` を明示的に選択し、日本語になることを確認します。
5. `auto`、`en`、`ja` を再起動なしで切り替え、開いているツール ウィンドウが即時に更新されることを確認します。その後再起動し、明示的に選択した値が復元されることを確認します。

## メニューとコマンド

1. **ツール > ContextRelay** のメニューと各コマンドが読みやすい名前で表示され、`%ContextRelay.*%` の未解決トークンが表示されないことを確認します。
2. Visual Studio が英語のときは英語、日本語のときは日本語になり、その他の言語では英語にフォールバックすることを確認します。

## 判定

英語版 [e2e_checklist.md](e2e_checklist.md) の全項目を、サポート対象の各 Visual Studio バージョンで完了してからリリースします。
