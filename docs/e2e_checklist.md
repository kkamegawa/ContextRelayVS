# ContextRelay for Visual Studio E2E Checklist

Use this checklist against a Visual Studio Experimental Instance before publishing a release.

## Prerequisites

- Visual Studio 2022 or Visual Studio 2026 / Insider with the extension installed from `ContextRelay.VSExtension.vsix`
- A test Microsoft 365 tenant with delegated Graph permissions configured for the app registration
- GitHub Copilot for Visual Studio installed if handoff integration is being checked
- A solution opened in Visual Studio

## Installation and load

1. Install the generated VSIX.
2. Start Visual Studio with `/rootsuffix Exp`.
3. Confirm the extension appears in **Extensions > Manage Extensions**.
4. Open **View > Other Windows > ContextRelay** and verify the tool window loads without exceptions.

## Commands and menus

1. Open **Tools** and confirm every ContextRelay command is present.
2. Confirm the **Tools > ContextRelay** menu and every command in it show readable names, never a raw `%ContextRelay.*%` token. With the Visual Studio UI language set to English the names are English; with the Japanese language pack installed and selected (**Tools > Options > Environment > International Settings**) they are Japanese, for example チャットにファイルを添付 and キャッシュをクリア. The name follows the Visual Studio language, not the operating system language, and any other language shows English. A raw token means Visual Studio did not resolve the metadata on that channel; record the Visual Studio version and channel.
3. Run **ContextRelay** and **Search Microsoft 365** and confirm the tool window opens.
4. Run **Clear Chat**, **Clear Cache**, **Clear Snippets**, **Generate Handoff Docs**, **Open Handoff Doc**, **Copy Handoff Prompt**, and **Show Debug Log**.
5. Confirm the commands do not throw and update UI state as expected.

## Theme and visual consistency

1. Repeat this section on Visual Studio 2022 and Visual Studio 2026 / Insider with the Light, Dark, Blue, and High Contrast themes.
2. Confirm the tool window background, panel surfaces, cards, list items, text, and borders use Visual Studio theme resources without default WPF white or black surfaces.
3. Confirm neutral buttons show distinct rest, pointer-over, pressed, keyboard-focus, and disabled states while the composer's primary button is the only primary action. That one button reads **Send** (送信) while idle and **Stop** (停止) while a response is generating.
4. Use Tab and Shift+Tab to reach each regular action button, then activate it with Enter and Space. Confirm slash-command suggestion items remain outside the Tab order and continue to follow the query input keyboard behavior. With the suggestion popup closed, confirm Tab moves focus out of the query box to the primary button, including while a response is generating; with the popup open, confirm Tab still applies the selected suggestion.
5. Confirm every focus indicator is visible in Light, Dark, Blue, and High Contrast themes and is not communicated by color alone.
6. Select query text by mouse drag, Shift+Arrow, and Ctrl+A. Confirm the selection foreground and background remain readable, and confirm the caret stays visible when the selection is collapsed.
7. Hover and select chat, search-result, snippet, and suggestion rows. For slash-command suggestions, move the pointer outside the popup and press `Up` and `Down` repeatedly. Confirm the active row has a visible full-row highlight, its foreground remains readable, and the highlight follows the selected item when a list contains more items than the visible viewport.
8. Change the Visual Studio theme while the tool window remains open. Confirm every surface and interaction state updates without reopening the window.
9. Narrow the tool window until action rows wrap. Confirm buttons keep consistent height and spacing and no content overlaps or clips.
10. Populate a long chat history and scroll through it. Confirm pixel scrolling remains smooth and existing chat rendering behavior is unchanged.

## Search and shared state

### Chat options and `/ask` parity (Issue #184)

1. Open **Tools > Options > ContextRelay > General** and confirm the `Chat` category contains **Maximum attached files** (default `5`), **Attach active editor** (default disabled), and **Stream chat responses** (default enabled).
2. Set the maximum to `0`, reload the Options page, and confirm the value remains non-negative and file attachments are disabled. Restore it to `5`.
3. Enable **Attach active editor** with a saved supported file open, then send `/ask` with no pinned snippet. Confirm the active editor is used as explicit context; with no eligible active editor, confirm `/ask` is rejected without an API request.
4. Click the **+** (attach file) button, select a supported workspace file, and confirm a pending attachment chip appears. Remove the chip and confirm it is no longer included in the next request.
5. Add pending local files and pinned snippets, send `/ask`, and confirm the request uses the bounded attachment set and the response displays the context labels.
6. Start a chat request with **Stream chat responses** enabled and confirm the response text is updated incrementally while the request is in progress, rather than appearing only after completion.
7. While a request is generating, confirm the primary button's label changes to **Stop** without moving or resizing, that keyboard focus stays on it when the change happens so Enter and Space stop the request, and that the streaming text uses the tool window foreground color in every theme. Add another attachment, stop the request with **Stop**, then start a new chat request and confirm the attachment added during generation is still present and is included in that next request.
8. After stopping a request, send a fresh query and confirm it succeeds; no automatic continuation request is made after the stop.
9. Toggle **Stream chat responses** and confirm both settings persist in `%AppData%\ContextRelay\settings.json`; verify an existing JSON file with none of the three properties loads with defaults `5`, disabled, and enabled.
10. Open **Tools > ContextRelay > Attach File to Chat**, select a supported workspace file, and confirm it produces the same pending attachment chip as the **+** button in the tool window.
11. Send a plain chat message (no slash command) with one attachment and ask for information the attached file does not contain. Confirm the reply is built from the attached material and states that the attached context is not sufficient, instead of answering from web results. Then send a plain chat message with no attachment, no pinned snippet, and active-editor attachment disabled, and confirm it still returns an answer.

### Core chat and shared state

1. Sign in with a valid Entra ID work/school account.
2. Run plain text such as `Summarize my current planning context`, then run `/mail test`, `/teams test`, `/sharepoint test`, `/onedrive test`, `/all test`, `/ask summarize`, `/workiq What meetings do I have today?`, and `/clear`.
3. Verify plain text produces a Microsoft 365 Copilot chat reply without source-search result cards.
4. Verify slash commands use the requested source filter, help text, result rendering, and clear behavior match the VS Code grammar.
5. Verify consecutive `/workiq` turns preserve Work IQ context until `/clear` or **Clear Chat** is used.
6. Pin a snippet and confirm it appears in `%LocalAppData%\ContextRelay\shared\snippets.json`.
7. Submit a chat turn after pinning context and confirm the assistant message shows the context labels.
8. Use assistant reply **Copy**, **Append**, and **Replace** actions for both Copilot and Work IQ replies, and confirm no editor content changes until one of these actions is clicked.
9. Submit a query and confirm history appears in `%LocalAppData%\ContextRelay\shared\chat-history.json`.
10. With the VS Code extension running, verify snippet/history changes propagate both directions.

## Handoff flow

1. Generate handoff docs with a solution open.
2. Confirm `.contextrelay\PLAN.md`, `TASKS.md`, and `TEST_PLAN.md` are created under the solution root.
3. Confirm `%LocalAppData%\ContextRelay\shared\handoff-index.json` is updated.
4. Run **Open Handoff Doc** and verify the file opens in Visual Studio.
5. Run **Copy Handoff Prompt** and verify the clipboard contains the expected prompt text.

## Diagnostics

1. Open **ContextRelay Debug** output pane.
2. Enable graph debug logging and verify request/response summaries are written without secrets.
3. Enable Work IQ debug logging and verify only structural metadata (status, task ID, context ID) is written, not prompt or reply bodies.
4. Force an auth or API failure and confirm the error is surfaced to the user and logged.

## Result

Ship only after every step above passes on each supported Visual Studio version.
