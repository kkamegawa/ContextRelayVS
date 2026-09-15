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

## Options と共有設定

Tools > Options > ContextRelay > General の `Chat` カテゴリで次を設定できます。

| 設定 | 既定値 | 内容 |
|---|---:|---|
| `ChatMaxAttachedFiles` | 5 | 1回のチャットに添付できるファイル数。負数は0として扱う |
| `ChatAttachActiveEditor` | false | 保存済みの対象アクティブ エディターを自動添付 |
| `ChatStreamResponses` | true | 応答を受信中に表示 |

設定は `%AppData%\ContextRelay\settings.json` に既存項目と同じ JSON オブジェクトとして保存します。新しい項目がない既存ファイルは上記の既定値で読み込むため、既存設定との互換性を維持します。

## 検証

[docs/e2e_checklist.md](e2e_checklist.md) で、Options の保存、添付上限、アクティブ エディター添付、コンテキストなし `/ask` の拒否、ストリーミング切り替えを Visual Studio 2022 と Visual Studio 2026 の Experimental Instance で確認します。
