# アーキテクチャ決定記録

## 2026-09-22 — chore/packageupdate: `dotnet test` で Microsoft.Testing.Platform ランナーを選択

- コンテキスト: `xunit.v3` を 3.2.2 から 4.0.1 へ（`xunit.runner.visualstudio` も 4.0.0 へ）更新した結果、テスト プロジェクトの依存関係が `xunit.v3.mtp-v1` から `xunit.v3.mtp-v2` に切り替わり、`Microsoft.Testing.Platform` 2.4.0 が引き込まれた。このバージョンの `Microsoft.Testing.Platform.MSBuild` ターゲットは、.NET 10 SDK 以降で新しい `dotnet test` ネイティブ モードに opt-in していない場合、無条件でビルド エラー（"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later"）を発生させる。ローカル環境（.NET 11 SDK）と CI（`windows-latest`。リポジトリに `global.json` がなかったため .NET 10 以降の SDK が解決される）の両方で、`dotnet test` が 1 件もテストを実行せずに失敗していた。
- 決定: リポジトリ ルートに `global.json` を追加し、`"test": { "runner": "Microsoft.Testing.Platform" }` を指定した。`Microsoft.NET.Test.Sdk` は維持する（`xunit.v3.core.mtp-v2` が明示的に要求する `<OutputType>Exe</OutputType>` を引き続き供給するため）。`coverlet.collector`（VSTest 用データ コレクターで MTP では動作しない）は `coverlet.MTP` 10.0.1 に置き換えた。`.github/workflows/ci.yml` と `release.yml` も .NET 10.x SDK を 8.0.x と併せてセットアップし、`ci.yml` の `Test core` ステップは VSTest 用引数（`--logger trx`、`--collect:"XPlat Code Coverage"`）を MTP 用引数（`--report-xunit-trx`、`--coverlet --coverlet-output-format cobertura`）に置き換え、`coverlet.MTP` が出力ファイル名にタイムスタンプを付与することから Codecov のグロブを `coverage.cobertura*.xml` に広げる必要があるが、これらのワークフロー変更はリポジトリ管理者が別途このコミットとは分けて適用する。
- 理由: `dotnet test` を MTP モードで実行（`DOTNET_TEST_RUNNER=Microsoft.Testing.Platform dotnet test ...`）したところ、既存の 318 件のテストはすべて無改修で合格した。これにより、今回の失敗が純粋にテスト ランナー選択の問題であり、テスト コードや製品コードの不具合ではないことを確認した。MSTest への移行も検討したが却下した。MSTest 4.x も同じ MTP v2 基盤の上に構築されており、同一の `global.json` opt-in が必要になるため今回の変更を回避できず、318 件全テストの属性・アサーションを書き換えるコストだけが追加で発生する。
- 影響: ローカルで `dotnet test` を実行するには .NET 10 SDK 以降が必須となった。生成されるカバレッジ ファイル名は固定の `coverage.cobertura.xml` ではなくタイムスタンプ付きになる。`release.yml` の `Test core` ステップはビルド系引数のみを渡しているため引数変更は不要。上記のワークフロー ファイルが更新されるまで、CI は引き続き VSTest エラーで失敗する。

## 2026-09-22 — Issue #192: 自動モードで Visual Studio の表示言語に従う

- コンテキスト: ユーザーは、自動言語選択が Visual Studio の表示言語に従うことを確認した。拡張機能プロセスのカルチャは Windows の設定に従う場合があり、英語版 Visual Studio 内で日本語 UI が表示される可能性がある。
- 決定: 既存のインプロセス パッケージが Visual Studio シェルから UI ロケールを取得し、セッション内のブローカー サービスを通じて公開する。アウトオブプロセスのツール ウィンドウは ViewModel を作成する前に言語を解決する。明示的な英語・日本語の選択は即時に適用し、Visual Studio の表示言語が未対応または取得できない場合は英語へフォールバックする。Options も同じシェル ロケールを使用する。ホストの表示言語は共有ユーザー設定に保存しない。
- 理由: これはユーザーが確認した言語契約を実装し、Issue #191 で記録されたプロセス カルチャの制限を置き換える。セッション単位のサービスにより、同時に実行される複数の Visual Studio インスタンスを互いに独立させられる。
- 追補: Experimental Instance で、保存済みの明示的な `ja` によりツール ウィンドウが日本語のままとなり、自由入力の `auto`/`en`/`ja` 欄ではその状態が分かりにくかった。Options の UI language を、サポートする言語（Auto、English、日本語）の固定ドロップダウンに変更した。保存値は変更しない。
- 検証: 英語・日本語のホスト ロケール、未対応および取得不能なロケール、プロセス カルチャとホスト言語が異なる場合、明示的な上書き、初期ラベル、ブローカー登録、パッケージ化されたリソースを検証する。Visual Studio で実際のツール ウィンドウも確認する。

## 2026-09-21 — Issue #191: Visual Studio の言語パックからコマンド名とメニュー名をローカライズ

- コンテキスト: あるビルド手順が `extension.json` 内のすべての `%ContextRelay.*%` 表示トークンを英語に置き換えていた。Visual Studio の一部のチャネルで未解決トークンがそのまま表示されたためであり、その結果 Tools > ContextRelay メニューをローカライズできなくなっていた。また、日本語リソース ファイルは 1 つのコマンドにしか存在しなかった。
- 決定: `extension.json` のすべての表示トークンを維持し、`.vsextension/string-resources.json`（英語、フォールバック）と `.vsextension/ja/string-resources.json` の両方に 15 個すべてを定義する。Visual Studio はインストールおよび選択された言語パック、つまり自身の UI 言語に基づいて解決するため、メニューはオペレーティング システムの言語ではなく Visual Studio の言語に従い、それ以外の言語では英語へフォールバックする。置換パッチは削除する。`ValidateContextRelayMenuMetadata` ビルド タスクは、マニフェスト トークン、既定リソースのキー、日本語リソースのキーが同一集合であることを、ビルド出力と VSIX 内にパッケージされたコピーの両方で検証し、不一致ならビルドを失敗させる。
- 理由: ユーザーはオペレーティング システムではなく Visual Studio の言語パックを切り替え、その言語にメニューが従うことを期待している。これを実現できるのは Visual Studio によるトークン解決だけである。
- 結果: Visual Studio の対象チャネルがメタデータを解決しない場合、メニューには未加工のトークンが表示される。これはリリース前に Experimental Instance で検証する。トークンごとの英語フォールバックを復元するとローカライズを諦めることになるため、明示的な判断が必要である。ツール ウィンドウの言語は別の設定であり、`UiLanguage` = `auto` は拡張機能プロセスのカルチャに従っていたため、Visual Studio の言語パックとは一致しない可能性があった。明示的な選択は引き続き利用できる。

## 2026-09-19 — Issue #184: 明示的コンテキストのチャット要求をグラウンディングし Web コンテキストを無効化

- コンテキスト: 通常のチャットと `/ask` は 1 つのペイロード ビルダーを共有し、同じ明示的コンテキスト（ピン留めスニペット、キューに入れた添付、`#file` メンション、アクティブ エディター）を両方の経路に渡せるようになった。それでも Copilot が添付資料ではなく Web 結果に基づいて回答する可能性があった。
- 決定: 要求が明示的コンテキストを含む場合、送信メッセージに `ChatContextPayloadBuilder.GroundingInstructionText` を追加し、その要求の `CopilotWebContext.IsWebEnabled` を `false` にする。明示的コンテキストがない要求は変更せず、グラウンディング指示も Web コンテキストの上書きも行わない。
- 理由: 添付ファイルとピン留めスニペットはユーザーが明示的に添付した情報であるため、主な情報源にする必要がある。グラウンディングされた要求で Web コンテキストを無効化すると、ユーザーが共有を選択した資料の範囲で回答できる。
- 結果: グラウンディングされたプロンプトは元のユーザー入力と異なるため、履歴の正規化と診断は合成後の要求メッセージを対象にする。グラウンディングされた要求では Copilot の Web 結果を使用しないことを文書化する。`/ask` のコンテキスト確認は Copilot トークン取得前に実行するため、明示的コンテキストのない要求は認証なしで拒否される。また、リダイレクトされたディレクトリによって信頼済みワークスペースの集合を拡張できないよう、信頼済みルートは最終ディレクトリの実体を比較する。

## 2026-09-15 — Issue #184: Visual Studio の `/ask` 設定を VS Code と一致させる

- コンテキスト: Visual Studio 拡張機能は、すべてのホストで 1 つの共有設定ファイルを維持しながら、VS Code 拡張機能と同じ明示的コンテキスト規則を適用する必要がある。
- 決定: 既存の設定オブジェクトに `ChatMaxAttachedFiles`（既定値 `5`、0 以上に制限）、`ChatAttachActiveEditor`（既定値 `false`）、`ChatStreamResponses`（既定値 `true`）を保存する。`/ask` は制限されたピン留め、保留ファイル、任意の保存済みアクティブ エディターのコンテキストを使用し、使用可能な明示的コンテキストがない場合は拒否する。通常のチャットはコンテキストなしでも実行できる。送信要求は含めた保留添付をアトミックに確保し、生成開始前に表示中の保留キューから削除する。
- 理由: 既存の設定ファイルを読み続けられる加算的な JSON 契約により、`/ask` の動作とユーザー設定をエディター間で一貫させられる。
- 結果: Options ページは 3 つの Chat 設定を公開し、呼び出し側は `/ask` 要求の構築時に保存された添付上限とストリーミング設定を守る。生成中に追加されたファイルは次の要求用にキューに残り、送信中の添付とは別に上限を使用する。

## 2026-08-02 — Issue #164: Remote UI の候補選択をインデックスで同期

- コンテキスト: Remote UI の `ListBox.SelectedItem` を候補オブジェクトにバインドすると、拡張機能側の選択は更新されるが、Remote UI 境界ではオブジェクト参照の同一性が保証されないため、WPF コンテナーの `IsSelected` 状態が確実に有効にならなかった。
- 決定: 選択された候補の表示用スカラー インデックスをシリアライズされた ViewModel 契約に公開し、`ListBox.SelectedIndex` に一方向でバインドする。コマンドの適用とヘルプ テキストには既存の候補オブジェクトとキーボード動作を維持する。
- 理由: 動的な Visual Studio の選択背景および前景トリガーを描画するには、選択されたコンテナーが `IsSelected` になる必要がある。
- 結果: これは ViewModel を変更しないという元の実装制約を意図的に変更するが、公開拡張機能 API とキーボード割り当ては変更しない。

## 2026-08-02 — Issue #164（追補）: `Selector.SelectedIndex` を項目ごとの `IsSelected` DataTrigger に置換

- コンテキスト: 前記の `SelectedIndex` 修正でも、候補ポップアップで Up/Down を押したときに行が見た目上選択されなかった。原因は、`VisibleCommandSuggestions` がキー入力ごとに新しい配列へ再代入され（`UpdateVisibleCommandSuggestions`）、`ItemsSource` が置き換わるたびに WPF の `Selector` が `SetCurrentValue` を通じて `SelectedIndex` を `-1` にリセットすることだった。`SetCurrentValue` はデータ バインディングの基底値ではなく有効値だけを上書きするため、その後に同じインデックスを渡す `Mode=OneWay` バインディングは無操作となり、リセット値の `-1` が残る。そのため `ListBoxItem.IsSelected` は `true` にならず、`FluentListBoxItemStyle` の `IsSelected` トリガーも発火しない。
- 決定: ポップアップでは `Selector` の選択に依存しない。`SlashCommandSuggestion` は `NotifyPropertyChangedObject` を継承し、シリアライズされる `IsSelected` bool を公開する。ViewModel は `SelectedCommandSuggestion` の変更時に値を直接切り替える。各候補行の XAML `DataTemplate` は `ListBox.SelectedIndex` / `ListBoxItem.IsSelected` ではなく、`IsSelected` の `DataTrigger` で選択背景と前景を描画する。
- 理由: `DataTrigger` はバインドされたデータ値に直接反応するため、`Selector` が `ItemsSource` 交換時に暗黙に行う選択リセットや、Remote UI におけるコレクションとスカラー プロパティの更新順序・タイミングの影響を受けない。
- 結果: `SelectedVisibleCommandSuggestionIndex` と `CalculateVisibleSelectionIndex` は不要なため削除した。キーボード移動（`MoveCommandSelection`）、表示スクロール範囲（`CalculateVisibleWindowStart`）、コマンド バインディングは変更しない。これは「スカラー `SelectedIndex` を公開する」方式を置き換えるものであり、`.agents/skills/wpf-fluent-vs-extension-ui/SKILL.md` の不変条件も Remote UI の候補リストでは `Selector.SelectedIndex` より項目ごとの `IsSelected` 状態を優先するよう更新した。

## 2026-08-02 — Issue #164（第 2 追補）: 選択変更時は常に `VisibleCommandSuggestions` を再代入

- コンテキスト: `IsSelected` の `DataTrigger` 修正を実際の Visual Studio Experimental Instance で確認しても、キーボードで選択した行が見た目上強調されなかった。原因は、`SelectCommandSuggestion(int index)` が表示スクロール範囲を動かす必要がある場合だけ `UpdateVisibleCommandSuggestions()` を呼んでいたことだった。同じ 4 件の範囲内で Up/Down を押す多くの場合、`VisibleCommandSuggestions` は再代入されず、送信済みの `SlashCommandSuggestion` オブジェクトの `IsSelected` だけがインプレースで変更されていた。これは、すでに Visual Studio プロセスへ送信されたコレクション内のネストしたオブジェクトに対して Remote UI が `PropertyChanged` を転送するという、これまで使用していなかった経路に依存しており、実際には機能しなかった。
- 決定: `SelectCommandSuggestion` は選択変更後、スクロール範囲が移動したかどうかに関係なく、常に `UpdateVisibleCommandSuggestions()` を呼ぶ。`IsSelected` の設定を新しい `internal static ApplySelectionFlag(IReadOnlyList<SlashCommandSuggestion> window, SlashCommandSuggestion? selected)` ヘルパーに集約し、表示用の新しい配列を `VisibleCommandSuggestions` に代入する直前に `UpdateVisibleCommandSuggestions()` から呼ぶ。`SelectedCommandSuggestion` setter から重複する項目単位の切り替えを削除する。
- 理由: 選択変更ごとにコレクション全体を再代入することで、インプレースの項目変更通知に依存せず、ViewModel 全体で実績のあるコレクション更新パターン（`ChatHistory`、`SearchResults`、`Snippets`、スクロールまたは入力時の同じコレクション）を再利用できる。
- 結果: Up/Down キーを押すたびに、最大 4 件の表示範囲が常に再送されるため、Remote UI の通信量が少し増える。`ApplySelectionFlag` は `CalculateVisibleWindowStart` と同じ方法でリフレクションによる単体テストが可能である。

## 2026-08-02 — Issue #164（第 3 追補）: 送信済みインスタンスを再利用せず候補表示項目を再構築

- コンテキスト: 前記の常時再代入修正後も、キーボードで選択した行が見た目上強調されなかった。原因は、`UpdateVisibleCommandSuggestions` が選択変更ごとに新しい配列を作っていたものの、その配列にすでに Visual Studio プロセスへ送信された同じ `SlashCommandSuggestion` インスタンスを含めていたことだった。Remote UI は送信済みオブジェクトを同一性で追跡するため、既知のインスタンスの現在のプロパティ値を再シリアライズしない。そのため `ApplySelectionFlag` が行った `IsSelected` のインプレース変更は表示側に届かなかった。一方、初回描画では生成直後の項目に選択状態を設定すると再描画され、`DataTrigger` 自体は機能していることが確認できた。この ViewModel で確実に描画されるコレクション（`ChatHistory`、`SearchResults`、`Snippets`）は、代入のたびに新しい項目オブジェクトを構築している。
- 決定: `UpdateVisibleCommandSuggestions` は、`SlashCommandSuggestion.CreateDisplayClone(source, isSelected)` による新しい表示用クローンから表示範囲を構築する。これはリフレクションでテスト可能な `ContextRelayWindowViewModel.BuildVisibleWindow(suggestions, windowStart, maxVisibleCount, selected)` を通じて行い、選択状態は生成時に埋め込む。各クローンの `ApplyCommand` は対応するマスター候補を適用するよう接続するため、選択管理（`commandSuggestions` に対する `IndexOf`）は正規のインスタンスを使い続けられる。`ApplySelectionFlag` は削除し、マスター項目には選択状態を設定しないため、表示範囲外に古い `IsSelected` 状態が残ることもない。
- 理由: 更新ごとに新しいインスタンスを構築することが、Remote UI 境界で再描画されることを確認済みの唯一のコレクション更新パターンである。同一性に基づく重複排除により、コレクションを再代入しても送信済みインスタンスの再利用は表示側から見えない。
- 結果: Up/Down キーを押すたびに、最大 4 個の新しい `SlashCommandSuggestion` と `AsyncCommand` インスタンスがシリアライズされる。XAML の `IsSelected` `DataTrigger` は変更しない。この決定は第 2 追補のインプレース設定を置き換えるものであり、`.agents/skills/wpf-fluent-vs-extension-ui/SKILL.md` の不変条件も、コレクションの再代入だけでなく新しい項目オブジェクトの構築を要求するよう拡張した。
