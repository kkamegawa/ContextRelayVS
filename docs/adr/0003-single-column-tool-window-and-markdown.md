# ADR 0003: Use a single-column tabbed tool window and render answers as safe Markdown blocks

[日本語](0003-single-column-tool-window-and-markdown_ja.md)

## Record Metadata

- Status: accepted
- Owner: unknown
- Created: 2026-10-25
- Last updated: 2026-10-25
- Documentation approval: approved (2026-10-25, user approval at the #224 design gate)
- Tracking: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223), [Issue #226](https://github.com/kkamegawa/ContextRelayVS/issues/226)
- Related records: [Design plan §4](../plan.md#4-ui), [ADR 0001](0001-sharepoint-embedded-retrieval.md)

## Context and Requirements

- Verified implementation today: the tool window has two columns (chat on the left, search results and pins on the right). Answers are plain `TextBlock` text and cannot be selected. The composer has unconditional Enter/Up/Down/Tab/Escape key bindings.
- The tool window is usually docked narrow. At about 320px the two columns leave too little width for either side.
- Remote UI runs the XAML in the Visual Studio process. It has no code-behind or event handlers, cannot reference custom controls, and binds only `DataContract`/`DataMember` types (S1).
- The reference extension renders answers with Markdig, HTML disabled, as Remote UI block view models updated in place while streaming (S2). It has no quotes, tables, links or text selection.
- Embedded results carry several extracts that users need to read and copy.

## Alternatives

| Option | Requirement fit | Benefits | Costs / risks | Evidence |
|--------|-----------------|----------|---------------|----------|
| Single-column Chat / Search results / Pinned tabs, Markdown blocks | Full | Full width for each task; selectable answers | State must live on the view model because tabs discard visuals | S1, S2 |
| Keep two columns, add Markdown | Partial | Smaller change | Still cramped at 320px | Current XAML |
| Render Markdown through a WebView | Poor | Full Markdown fidelity | Not available in Remote UI without custom controls; theming and security cost | S1 |

## Current Decision

- **Layout**: replace the two columns with one `TabControl` bound to `SelectedTabIndex`. Searching switches to Search results and sending a chat switches to Chat. Lists, preview expansion and selection live on the view model. Inactive tabs show new-item counts. Help text appears only when the window is empty or on error. No horizontal scrolling at 320px.
- **Composer**: a multiline text box with one Enter key binding dispatched by `ConfirmQueryInputCommand`, which accepts a suggestion or sends. Shift+Enter inserts a newline. Up/Down/Tab/Esc are bound only while suggestions or the source selector are visible; otherwise their commands are null so the keys reach the text box. IME composition must not send.
- **Result cards**: title, source badge (Embedded shows the source name and Preview), sensitivity label, summary, and Preview / Pin / Open. The existing card context menu (Pin, Copy, Append to handoff, Open in browser) is kept. Preview shows every extract as selectable text with Copy. Open accepts only URLs that pass `ExternalUrlSafety`.
- **Markdown**: Markdig with `DisableHtml()`, referenced only by the net8 out-of-process extension so it never loads into devenv.
  - Blocks: headings 1-3, paragraphs, lists nested up to three levels, quotes (keeping their child blocks), code blocks, separators, and pipe tables rendered as aligned monospace text.
  - Inline: emphasis becomes plain text, inline code keeps its backticks, images keep only alt text, HTML is removed.
  - Links: the text stays in the paragraph; validated https links are listed as buttons under the answer. Rejected links stay plain text.
  - Streaming re-parses the accumulated answer and updates blocks in place. An unclosed fence renders as an open code block. Copy and editor insertion use the raw answer.
- **Selectable text**: a new display-only read-only `TextBox` style whose `PART_ContentHost` is a `Decorator`, not a `ScrollViewer`, with `IsTabStop=False`. Wheel scrolling over the list and Tab traversal keep working. Only code blocks and tables get a horizontal `ScrollViewer`.
- **Remote UI contract**: every new view model uses `DataContract`/`DataMember`; visibility uses precomputed booleans with `BooleanToVisibilityConverter` or data triggers; selection updates send new item instances.

## Consequences and Review Triggers

- Scroll positions reset when switching tabs, except that Chat scrolls to the latest message.
- Inline links cannot be rendered inside paragraphs without code-behind, so links are listed below each answer.
- Markdig becomes a new dependency of the out-of-process extension and must pass the dependency audit.
- Reconsider if Remote UI gains rich-text or inline hyperlink support, or if users need table cell selection.

## Sources

| ID | Title | Exact URL | Checked on | Relevant claim / section | Relevance to this decision |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | Why Remote UI | https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/inside-the-sdk/remote-ui | 2026-10-25 | No code-behind or event handlers; no custom controls; `DataContract` types only | Constrains Markdown rendering and key handling |
| S2 | vsextensionforcodex | https://github.com/kkamegawa/vsextensionforcodex | 2026-10-25 | `SafeMarkdownService`: Markdig with HTML disabled, block view models, in-place streaming updates | Reused rendering approach |

## Change History

This record uses approved in-place updates rather than append-only replacement ADRs. Preserve all history rows.

| Date | Previous decision summary | New decision summary | Reason | Source IDs | Approval reference |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | Initial proposal | Single-column tabs and safe Markdown blocks | Narrow docking, selectable answers and Embedded previews | S1, S2 | User approval, 2026-10-25 (#224) |
