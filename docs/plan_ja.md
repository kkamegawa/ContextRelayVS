# ContextRelay for Visual Studio — Issue #184 設計要約

この文書は [docs/plan.md](plan.md) 全体の日本語訳ではなく、Issue [#184](https://github.com/kkamegawa/ContextRelayVS/issues/184) に関する設計の要約と補足です。

## 製品方針

Visual Studio 版は、VS Code 版と同じスラッシュコマンド、チャット、添付コンテキスト、ハンドオフ文書、共有ストアの動作を提供します。ライセンスは MIT です。

## `/ask` とチャット

- `/ask` はピン留めスニペット、保留中のローカル添付、設定で有効にした保存済みアクティブ エディターを明示的コンテキストとして使用します。
- 利用可能な明示的コンテキストがない `/ask` は実行せず、通常のチャットはコンテキストなしでも実行できます。
- 添付数は `ChatMaxAttachedFiles` で制限し、既定値は 5、0 は添付なしです。
- 送信対象の保留中添付はリクエスト送信時に画面上の保留キューから外します。応答生成中に追加したファイルは次のリクエスト用として保留し、送信中のファイルは添付上限の枠を使用しません。
- `ChatAttachActiveEditor` の既定値は false、`ChatStreamResponses` の既定値は true です。
- スラッシュコマンドなしの通常チャットも `/ask` と同じコンテキスト選択を使います。違いは、明示的コンテキストが無い場合でも実行を許す点だけです。
- 添付の優先順位は `#file` メンション → 保留中の添付 → アクティブ エディターで、正規化したパスで重複を除き、`ChatMaxAttachedFiles` で打ち切ります。`/workiq` は従来どおり `FileMentionResolver.MaxFileMentions` を使います。
- アクティブ エディターは保存済み内容のみを読み、選択範囲がある場合はその行だけを添付します。未保存の編集があるバッファーでは行番号が保存済みファイルと一致しないため、選択を無視します。
- 明示的コンテキストを含む要求では、`ChatContextPayloadBuilder.GroundingInstructionText` を送信メッセージに付加し、`CopilotWebContext.IsWebEnabled` を `false` にして Web 結果ではなく添付とピン留めを主情報源にします。明示的コンテキストが無い要求にはどちらも適用しません。
- 生成中の要求はツールウィンドウから停止でき、停止した要求はキャンセルとして報告します。継続要求は常に手動です。
- 直近の検索要約はあれば `additionalContext` に参考情報として追加しますが、グラウンディング コンテキストではなく、`/ask` の判定も Web コンテキストの無効化も行いません。
- `/ask` は Copilot トークン取得の前にペイロードを組み立てて検証するため、明示的コンテキストがない要求は認証やネットワーク処理なしにローカルで拒否します。

## Options と共有設定

Tools > Options > ContextRelay > General の `Chat` カテゴリで次を設定できます。

| 設定 | 既定値 | 内容 |
|---|---:|---|
| `ChatMaxAttachedFiles` | 5 | 1回のチャットに添付できるファイル数。負数は0として扱う |
| `ChatAttachActiveEditor` | false | 保存済みの対象アクティブ エディターを自動添付 |
| `ChatStreamResponses` | true | 応答を受信中に表示 |

設定は `%AppData%\ContextRelay\settings.json` に既存項目と同じ JSON オブジェクトとして保存します。新しい項目がない既存ファイルは上記の既定値で読み込むため、既存設定との互換性を維持します。

## UI 言語

- Options ページまたはツール ウィンドウの言語切り替えで指定した明示的な英語・日本語は、共有設定の値として正規化して適用します。
- `auto` では、インプロセス パッケージが Visual Studio シェルから表示言語を取得し、セッション内のブローカー サービスを通じてアウトオブプロセスのツール ウィンドウへ渡します。拡張機能プロセスや Windows のスレッド カルチャは判定に使用しません。
- Visual Studio の表示言語が未対応、または取得できない場合は英語へフォールバックします。言語解決は初期ツール ウィンドウの ViewModel 作成前に完了します。
- Options の UI language は自由入力ではなく固定のドロップダウンで、サポートする言語 `Auto (follow Visual Studio)`（`auto`）、`English (en)`（`en`）、`日本語 (ja)`（`ja`）から選択します。保存値は `auto` / `en` / `ja` で、既定は `auto` です。明示的な選択が常に優先されるため、保存済みの `en` / `ja` は `Auto` に戻すまで Visual Studio の表示言語より優先されます。
- Options のラベルも同じ Visual Studio の表示言語を使用し、設定変更時はプロパティ ディスクリプターを更新します。Visual Studio の表示言語自体は共有ユーザー設定には保存しません。

## 検証

[docs/e2e_checklist.md](e2e_checklist.md) で、Options の保存、添付上限、アクティブ エディター添付、コンテキストなし `/ask` の拒否、ストリーミング切り替えを Visual Studio 2022 と Visual Studio 2026 の Experimental Instance で確認します。
