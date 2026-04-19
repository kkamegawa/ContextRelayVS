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
3. Run **Clear Chat**, **Clear Cache**, **Clear Snippets**, **Generate Handoff Docs**, **Open Handoff Doc**, **Copy Handoff Prompt**, **Show Debug Log**, and **Open Settings**.
4. Confirm the commands do not throw and update UI state as expected.

## Options pages

1. Open **Tools > Options > ContextRelay**.
2. Verify the **General**, **Authentication**, **Cache**, and **Adapters** pages render.
3. Change values, save, reopen the dialog, and confirm persistence.

## Search and shared state

1. Sign in with a valid Entra ID work/school account.
2. Run `/mail test`, `/teams test`, `/sharepoint test`, `/onedrive test`, `/all test`, `/ask summarize`, and `/clear`.
3. Verify the source filter, help text, result rendering, and clear behavior match the VS Code grammar.
4. Pin a snippet and confirm it appears in `%LocalAppData%\ContextRelay\shared\snippets.json`.
5. Submit a query and confirm history appears in `%LocalAppData%\ContextRelay\shared\chat-history.json`.
6. With the VS Code extension running, verify snippet/history changes propagate both directions.

## Handoff flow

1. Generate handoff docs with a solution open.
2. Confirm `.contextrelay\PLAN.md`, `TASKS.md`, and `TEST_PLAN.md` are created under the solution root.
3. Confirm `%LocalAppData%\ContextRelay\shared\handoff-index.json` is updated.
4. Run **Open Handoff Doc** and verify the file opens in Visual Studio.
5. Run **Copy Handoff Prompt** and verify the clipboard contains the expected prompt text.

## Diagnostics

1. Open **ContextRelay Debug** output pane.
2. Enable graph debug logging and verify request/response summaries are written without secrets.
3. Force an auth or API failure and confirm the error is surfaced to the user and logged.

## Result

Ship only after every step above passes on each supported Visual Studio version.
