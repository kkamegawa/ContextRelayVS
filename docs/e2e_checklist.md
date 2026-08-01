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
2. Run **ContextRelay** and **Search Microsoft 365** and confirm the tool window opens.
3. Run **Clear Chat**, **Clear Cache**, **Clear Snippets**, **Generate Handoff Docs**, **Open Handoff Doc**, **Copy Handoff Prompt**, and **Show Debug Log**.
4. Confirm the commands do not throw and update UI state as expected.

## Theme and visual consistency

1. Repeat this section on Visual Studio 2022 and Visual Studio 2026 / Insider with the Light, Dark, Blue, and High Contrast themes.
2. Confirm the tool window background, panel surfaces, cards, list items, text, and borders use Visual Studio theme resources without default WPF white or black surfaces.
3. Confirm neutral buttons show distinct rest, pointer-over, pressed, keyboard-focus, and disabled states while the Search button is the only primary action.
4. Use Tab and Shift+Tab to reach each regular action button, then activate it with Enter and Space. Confirm slash-command suggestion items remain outside the Tab order and continue to follow the query input keyboard behavior.
5. Confirm every focus indicator is visible in Light, Dark, Blue, and High Contrast themes and is not communicated by color alone.
6. Select query text by mouse drag, Shift+Arrow, and Ctrl+A. Confirm the selection foreground and background remain readable, and confirm the caret stays visible when the selection is collapsed.
7. Hover and select chat, search-result, snippet, and suggestion rows. Confirm selection and pointer-over states have readable foreground/background pairs.
8. Change the Visual Studio theme while the tool window remains open. Confirm every surface and interaction state updates without reopening the window.
9. Narrow the tool window until action rows wrap. Confirm buttons keep consistent height and spacing and no content overlaps or clips.
10. Populate a long chat history and scroll through it. Confirm pixel scrolling remains smooth and existing chat rendering behavior is unchanged.

## Search and shared state

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
