_See the session plan at the project root (`plan.md` snapshot)._

This file mirrors the design plan stored during planning so that downstream work can reference it from the repository. Keep it in sync with session notes when major decisions change.

> **Note**: The authoritative, always-current design document is this file. Session-state copies are ephemeral. When you make a design change, update this file and open a PR.

---

# ContextRelay for Visual Studio — Design Plan

See [`../README.md`](../README.md) for the short description and [`shared-session-schema.md`](shared-session-schema.md) for the cross-editor sharing format.

## 0. Product intent

Port the VS Code extension `ContextRelay` to Visual Studio (2022 / 2026, Insider included) with the same UX, slash-command grammar, and handoff-document output. Share pinned snippets, chat history, and handoff-document pointers with the VS Code version on the same machine via a per-user shared store.

License: **MIT**.

## 1. Scope (parity with the VS Code version)

| VS Code feature | VS implementation |
|---|---|
| Plain Microsoft 365 Copilot chat | Plain text without a slash command starts or continues chat without implicit source search context |
| Keyword / slash-command search (`/mail` `/teams` `/sharepoint` `/onedrive` `/onenote` `/task` `/connectors` `/embedded` `/all` `/ask` `/clear`) | Slash commands remain explicit search or context operations |
| Opt-in SharePoint Embedded retrieval (`/embedded`, preview) | Direct Copilot Retrieval call with a dedicated token; excluded from `/all` (see §6.1) |
| Snippet pinning (persisted across sessions) | Cross-editor shared store (`%LocalAppData%\ContextRelay\shared\snippets.json`) |
| Handoff docs (`PLAN.md` / `TASKS.md` / `TEST_PLAN.md` / `HANDOFF.md`) | Written under solution root `.contextrelay/` (fallback `%USERPROFILE%\.contextrelay\`) |
| Chat tab (Copilot Chat API /beta) | Phase 2 (feature flag off in Phase 1) |
| Cache (TTL + LRU) | Re-implemented in C# |
| Debug output | VS output window pane "ContextRelay Debug" |

### Phasing

- **Phase 0**: Repo/solution scaffold, MIT LICENSE, CI, empty VSIX launches.
- **Phase 1 (MVP)**: Auth + Mail/SharePoint/OneDrive/Teams search + slash router + snippet pinning + handoff docs + settings + cache + debug log + cross-editor shared store.
- **Phase 2**: Copilot Chat API (plain chat and `/ask`), connectors, deeper Copilot-for-VS handoff, Japanese UI, slash-command discovery, result context actions, and explicit assistant reply editor actions.
- **Phase 3**: Marketplace publish, auto-update, opt-in telemetry.

## 2. Target environment

- Visual Studio 2022 (17.9+) and Visual Studio 2026 / Insider, Community / Professional / Enterprise on amd64 and arm64.
- Tool window, commands and Graph calls: an out-of-process VisualStudio.Extensibility extension on `net8.0-windows10.0.22621.0`.
- Options pages and the UI-language brokered service: an in-process VSSDK `AsyncPackage` on .NET Framework 4.8 (`net48`), loaded on demand.
- Core: `netstandard2.0`, shared by both processes, so dependencies that must not load into devenv stay out of Core.
- Microsoft 365 work/school accounts only (Entra ID).

## 3. Project layout

```
ContextRelayVS/
├─ src/
│  ├─ ContextRelay.VSExtension/          # net8 out-of-process extension and VSIX; Remote UI tool window, commands, host services
│  ├─ ContextRelay.VSExtension.Package/  # net48 in-process AsyncPackage; Options pages, UI-language brokered service
│  ├─ ContextRelay.Core/                 # netstandard2.0; adapters, auth, cache, router, snippets, handoff, shared store, settings
│  └─ ContextRelay.UI/                   # net8 WPF project with no sources; the tool window UI lives in ContextRelay.VSExtension
├─ tests/
│  └─ ContextRelay.Core.Tests/    # xUnit
├─ docs/                          # plan.md, shared-session-schema.md, tenant admin guide, etc.
├─ LICENSE                        # MIT
├─ README.md / README_ja.md
└─ ContextRelayVS.sln
```

## 4. UI

- One tool window hosting a Remote UI `RemoteUserControl` bound to a `DataContract` view model. Remote UI allows no code-behind, event handlers or custom controls, so every interaction is a command or binding (see [ADR 0003](adr/0003-single-column-tool-window-and-markdown.md)).
- Bound to `VsBrushes` / `EnvironmentColors` for automatic theme follow. For `RemoteUserControl`, apply Visual Studio theme resources explicitly in XAML instead of relying on default WPF control styling.
- **Layout**: a single column with `Chat` / `Results` / `Pinned` tabs (`ThemedTabItemStyle`), selected through a two-way `SelectedTabIndex`. Searching switches to `Results` and sending chat switches to `Chat`. The tab control discards non-selected visual trees, so list contents, expanded previews and selection live in the view model; scroll position resets on tab switch except that chat scrolls to the latest message. Inactive tabs show a new-item count. The header stays within one or two lines (account and status); help text appears only when chat and results are empty or on error. No horizontal scrolling at 320 px; buttons wrap.
- **Composer**: fixed at the bottom, in order: suggestions or Embedded source picker, attachment chips, then attach, input and send/stop. The input is multi-line (`AcceptsReturn`, minimum and maximum height). Enter sends and Shift+Enter inserts a line break. Enter has a single key binding routed through `ConfirmQueryInputCommand` (accept the suggestion or send). Up/Down/Tab/Escape are bound only while suggestions or the source picker are shown; otherwise their commands are null so the keys reach the text box. Enter during IME composition must not send.
- **Results**: one list of cards below the search summary, each card identifying its source with a badge (the results are not split into per-source sections).
- **Result cards**: title, source badge (Embedded shows the source name and `Preview`), sensitivity label, summary and date when present, and Preview / Pin or Unpin / Open actions. The card context menu (Pin, Copy, Append to handoff, Open in browser) stays alongside these buttons. Preview expands in place and shows every extract in a selectable read-only text box with a Copy button; the Pinned tab reuses it. Open accepts only https URLs that pass `ExternalUrlSafety`. The first Embedded search and the Embedded results heading show a short preview and pay-as-you-go notice.
- **Markdown answers**: Markdig with `DisableHtml()` and pipe tables, referenced only by the net8 out-of-process extension so it never loads into devenv.
  - Blocks: headings 1-3, paragraphs, list items nested up to three levels, block quotes (left rule, nested blocks kept), code blocks (monospace, no wrap, horizontal scroll, language label), thematic breaks, and pipe tables shown as column-aligned monospace text.
  - Inline: emphasis becomes plain text, inline code keeps its backticks, images keep only their alt text and are never fetched, HTML is stripped.
  - Links: the text stays in the paragraph; https links that pass `ExternalUrlSafety` are listed as buttons under the answer, and failing links stay plain text.
  - Streaming re-parses the full received text at an interval and updates blocks in place; an unclosed fence renders as an in-progress code block. Copy, insert/replace in the editor and continue use the raw answer text.
- **Text selection**: text blocks use a new display-only read-only `TextBox` style (no border, transparent background, existing VS resources) whose `PART_ContentHost` is a `Decorator` rather than a `ScrollViewer`, so the wheel scrolls the list, with `IsTabStop=False` so long answers do not add tab stops. Only code blocks and tables are wrapped in a horizontal `ScrollViewer`.
- **Remote UI contract**: new view models use `[DataContract]` / `[DataMember]`; visibility uses precomputed booleans with `BooleanToVisibilityConverter` or data triggers; selection updates send new item instances.
- VS Code-specific sidebar move commands are dropped; VS native dock/float/tab is used instead.

## 5. Auth

- `Microsoft.Identity.Client` + `Microsoft.Identity.Client.Broker` (WAM).
- Interactive (WAM) → Silent (cache) → Interactive fallback.
- Token cache under `%LocalAppData%\ContextRelayVS\msal.cache` via `MsalCacheHelper` + DPAPI.
- Same Graph scope set as the VS Code version.
- SharePoint Embedded uses a dedicated token for delegated `FileStorageContainer.Selected`, acquired by `GetEmbeddedAccessTokenAsync` in the same way as the Work IQ token. The scope is never added to `AuthScopeCatalog.BuildRequiredScopes`, so missing Embedded consent fails only `/embedded` (see §6.1 and [ADR 0001](adr/0001-sharepoint-embedded-retrieval.md)).
- Entra app-registration requirements documented in `docs/tenant_admin_quickstart.md`.

## 6. Graph calls

- `Microsoft.Graph` v6 for REST SDK. `Microsoft.Graph.Beta` for `/beta` (Chat, Retrieval where needed).
- Retrieval API (`POST /v1.0/copilot/retrieval`) and Chat API (`/beta/copilot/conversations`) called directly via `HttpClient`.

## 6.1 SharePoint Embedded retrieval

Ported from [kkamegawa/ContextRelay#287](https://github.com/kkamegawa/ContextRelay/issues/287). Decisions and sources are recorded in [ADR 0001](adr/0001-sharepoint-embedded-retrieval.md) and [ADR 0002](adr/0002-shared-store-schema-v2.md).

- **Settings**: `EmbeddedEnabled` (default `false`) and `EmbeddedSources`, a list of `Name`, `ContainerTypeId` and `Enabled`, stored in PascalCase like the other settings. One validator is shared by Options and the runtime: `Name` is 1-64 characters after trimming and unique ignoring case; `ContainerTypeId` is a GUID, normalized to lowercase `D` format and unique. Invalid entries are skipped at runtime and their count is shown in the tool window status.
- **Activation**: Embedded is active only when `EmbeddedEnabled` is true, `CloudEnvironment` is Global (the Retrieval API is not available in national clouds) and at least one enabled, valid source exists. `/embedded` appears in slash suggestions only while active and is never part of `/all`.
- **Pre-validation**: before authentication or network work, in order: activation, a selected source, and a query of 1-1500 characters after trimming (rejected, not truncated). The result count is `MaxResults` clamped to 1-25. Connectors keep their current 1500-character truncation.
- **Source selection**: one enabled source is selected automatically and shown as a chip above the input. With several, typing `/embedded` opens an inline picker above the input that lists display names only (never GUIDs), supports Up/Down/Enter/click, and preselects the source chosen earlier in the session. Escape clears the selection and closes the picker; sending without a selection asks for one and makes no request. Stop cancels the in-flight request through its `CancellationToken` and discards its result.
- **Token**: a dedicated delegated `FileStorageContainer.Selected` token from `GetEmbeddedAccessTokenAsync` (see §5). The first `/embedded` can trigger interactive consent. Consent or invalid-scope failures (AADSTS) fail only `/embedded`, name the scope, and point to the owning app's container type grant and the user's app registration. `Files.Read.All` and `Sites.Read.All` are not added for Embedded unless a tenant check proves them necessary, which is handled as a design change.
- **Request**: `POST {Graph}/v1.0/copilot/retrieval` with `queryString`, `dataSource: "sharePointEmbedded"`, `dataSourceConfiguration.sharePointEmbedded.containerTypeId`, `resourceMetadata: ["title"]` and `maximumNumberOfResults`. The data source and its pay-as-you-go billing are in preview. Sending, extract mapping and score handling are shared with connectors; SharePoint and OneDrive keep Graph Search.
- **Responses**: there is no fallback to other sources.

  | Response | Handling |
  |---|---|
  | 400 | Check the settings or preview availability |
  | 401 | Sign in again |
  | 403 | Check the owning app's container type grant, user access, content discoverability, billing and the semantic index |
  | 404 | Possible misconfiguration or preview not available; check the settings |
  | 429 | Retry through `SendWithRetryAsync` with Retry-After, then report the failure |

  Zero results are reported only for a 200 response with empty `retrievalHits`, with a short note on indexing and access.
- **Mapping**: hits are sorted by their highest extract `relevanceScore`, descending; hits without a score go last in their original order. The title is `resourceMetadata.title` or `Untitled`; `webUrl` is opaque and never parsed for a title. The summary is the first extract. All extracts (with order and score), `webUrl`, `sourceName`, `containerTypeId` and the sensitivity label name and ID are kept.
- **Identity**: `ContextItemKeys.Build` adds the container type for Embedded only, as `sharepoint-embedded::{containerTypeId}::{discriminator}::{title}`, so preview, pin, unpin and shared-store round trips agree.
- **Context use**: pinned Embedded extracts go to Copilot Chat as text in `additionalContext` within the existing character budget, never through `TryGetFileContextUri`; search results are not attached automatically. `AskPromptBuilder` and handoff documents include them as text, and handoff documents name the source. Embedded content is never sent to Work IQ.

## 7. Commands (.vsct)

VS Code command → VS command mapping:

| VS Code | VS |
|---|---|
| `openPanel` | `ContextRelay.OpenPanel` |
| `search` | `ContextRelay.Search` |
| `clearChat` | `ContextRelay.ClearChat` |
| `clearCache` | `ContextRelay.ClearCache` |
| `clearSnippets` | `ContextRelay.ClearSnippets` |
| `generateHandoffDocs` | `ContextRelay.GenerateHandoffDocs` |
| `openCopilotChat` | `ContextRelay.OpenCopilotChatWithPrompt` (clipboard + command-well invocation) |
| `openHandoffDoc` | `ContextRelay.OpenHandoffDoc` |
| `copyHandoffPrompt` | `ContextRelay.CopyHandoffPrompt` |
| `showDebugLog` | `ContextRelay.ShowDebugLog` |
| `openSettings` | `ContextRelay.OpenSettings` (opens Options page) |

Sidebar move commands (`moveToSecondarySideBar` etc.) are intentionally not ported — VS native docking replaces them.

## 8. Options (`DialogPage`)

Registered by an in-proc VSSDK `AsyncPackage` and persisted to the shared JSON store at `%AppData%\ContextRelay\settings.json` so both the Options dialog and the out-of-proc extension host read the same settings.

- **Pages**: `ContextRelay > General` is a property grid using `Localized*` attributes with English and Japanese resources, grouped as General, Chat, Authentication, Caching, Search sources, Diagnostics and Privacy. `ContextRelay > SharePoint Embedded` is a WPF page hosted by `UIElementDialogPage` (the net48 package sets `UseWPF`). It contains the enable checkbox, a prerequisites notice (preview, pay-as-you-go, Global only, owning-app grant), the source list (display name, shortened GUID, enabled) with Add / Edit / Remove, an edit form with per-field validation messages, and a warning when the cloud is not Global (values stay editable). Apply is cancelled and errors are shown when validation fails; Cancel discards edits and reloads the saved values. See [ADR 0004](adr/0004-shared-settings-updates-and-embedded-options.md).
- **Shared persistence**: every writer of the shared JSON file (both Options pages and the out-of-proc host) uses the `ContextRelaySettingsStore` update API, which reads the latest file, applies only the caller's changes and saves atomically, all under a cross-process named mutex. Each page changes only its own fields; whole-snapshot saves are not used, because `File.Replace` prevents torn writes but not lost updates.
- **Registration**: the `PatchPkgdefFile` build task sets the display name per page key, and `build/EnableUnifiedSettings.ps1` keeps `IsInUnifiedSettings=0` for both pages. `InProcPackageVsixPackagingTests` checks both registrations and display names in the built VSIX.
- **UI language**: changes in either the Options page or the tool window language toggle are normalized to the same shared setting value. Explicit English/Japanese choices take precedence. In automatic mode, the existing in-process package reads the Visual Studio UI locale and exposes it through a session-local brokered service to the out-of-process tool window. Unsupported or unavailable host languages resolve to English, independently of the operating system and extension thread cultures. Language resolution completes before the initial tool window view model is created. Option labels use the same host locale and the property descriptors refresh when the shared setting changes.
- **UI language selection**: the Options property is a fixed drop-down (not free text) listing the supported values: `Auto (follow Visual Studio)` (`auto`), `English (en)` (`en`) and `日本語 (ja)` (`ja`). The stored value stays `auto`, `en` or `ja`. The default is `auto`; because an explicit choice always wins, a saved `en` or `ja` keeps overriding the Visual Studio display language until `Auto` is selected again. The out-of-process host watches the shared settings file, so a language saved from Options reaches an already-open tool window: the host reloads the full language state (including the host locale for `auto`) and refreshes the view model without reopening the window. The watcher's coalescing logic (guaranteeing a save that arrives mid-reload is not lost, and that a failed reload retries with a bounded backoff instead of being dropped) lives in a small, independently testable `ReloadCoalescer` type rather than inline in the host. An already-open command/file-mention suggestion popup is rebuilt with the new language too, keeping the same selected row by its language-invariant command or file token, since its query text does not change on a language change alone. A status message already on screen (for example a result count) is reproduced in the new language when it was built from one of the fixed, argument-free status resources; a message carrying a captured argument or raw exception text is left unchanged rather than risk showing an incorrect or malformed value. A failed host-locale lookup falls back to English for a bounded cooldown (rather than retrying the broker and its timeout on every subsequent settings read) before it is retried, so a persistently unresponsive package does not repeatedly delay ordinary search, chat, file, and handoff operations. Reading the shared settings file for this comparison uses an API that distinguishes a legitimate default snapshot from a failed read (a concurrent save still in progress, corrupt content); a failed read is treated as a reload failure so it retries with backoff instead of silently comparing against a default that can spuriously equal the current language and hide a real save.
- **Chat settings**: the `Chat` category exposes `ChatMaxAttachedFiles` (default `5`, non-negative; `0` disables attachments), `ChatAttachActiveEditor` (default `false`), and `ChatStreamResponses` (default `true`). These values are persisted in the same JSON object and missing properties retain these defaults for existing settings files.

## 8.1 Chat context rules and parity settings (Issue #184)

The Visual Studio implementation uses the same explicit-context behavior as the VS Code extension. `/ask` accepts pinned snippets, pending local attachments, and the eligible saved active editor when enabled; it is rejected when no usable explicit context is available. Plain chat continues to work without explicit context. Response streaming is controlled by `ChatStreamResponses`, and the attachment count is bounded by `ChatMaxAttachedFiles`. Pending attachments are claimed and removed from the visible pending queue when the request is submitted, so files added during generation remain queued for the next request and do not compete with in-flight files for the limit.

The same context selection applies to plain chat, so an input without a slash command is built from the identical sources and differs only in that it is allowed to run with no explicit context at all:

- **Attachment selection**: `#file` mentions are taken first, then queued attachments, then the active editor, deduplicated by canonical path and truncated at `ChatMaxAttachedFiles`. `/workiq` keeps its own limit of `FileMentionResolver.MaxFileMentions`.
- **Active editor**: only saved file content is read, and a non-empty editor selection narrows the attachment to the selected lines. When the buffer has unsaved edits the selection is ignored, because its line numbers do not match the saved file.
- **Grounding**: when a request carries explicit context, `ChatContextPayloadBuilder.GroundingInstructionText` is appended to the outbound message and `CopilotWebContext.IsWebEnabled` is set to `false` so the answer is built from the attached files and pinned snippets instead of web results. Requests without explicit context send no grounding instruction and no web context override.
- **Search summary**: the latest search summary is added to `additionalContext` as orientation when one exists, but it is not grounding context. It does not satisfy the `/ask` check and does not disable web context.
- **Validation order**: `/ask` builds and validates its payload before acquiring a Copilot token, so a request with no explicit context is rejected locally without authentication or network work.
- **Request lifecycle**: the composer keeps one primary button that switches its label and behavior to stop while a request is running, so cancellation is reachable from the same keyboard position, and a stopped request reports cancellation without issuing an automatic continuation. Continuation stays manual.

## 9. Handoff docs

- Output under active solution `.contextrelay/` (fallback `%USERPROFILE%\.contextrelay\` when no solution is open).
- `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, optional `HANDOFF.md`. Timestamped appended sections, same format as VS Code version.
- `CopyHandoffPrompt` puts a Copilot-for-VS-ready prompt on the clipboard.

## 10. Cache and snippets

- `TtlLruCache<TKey, TValue>` — default TTL 300 s, max 200 entries, LRU eviction.
- Cache optional persistence to `.vs/ContextRelay/cache.json` (solution scope).
- Embedded results use a separate memory-only `TtlLruCache` with the same TTL and size settings, keyed by account (home account ID and tenant), cloud and Graph endpoint, container type, result count and query. They are never written to the persisted cache snapshot.
- Sign-in, sign-out, account switch, changes to client ID, tenant, cloud, endpoint, `EmbeddedEnabled` or `EmbeddedSources`, and Clear Cache discard the Embedded cache and displayed Embedded results. Each request carries a generation number; responses from an older generation or arriving after a discard are neither shown nor cached, and a request invalidated by a settings change reports that in a message.
- **Snippets persisted to the cross-editor shared store** (see §11), not a private VS file.

## 11. Cross-editor session sharing

See [`shared-session-schema.md`](shared-session-schema.md) for the authoritative schema.

- Scope: **data only** (snippets, chat history, handoff-document pointers). No tokens, no raw Graph responses.
- Storage root: `%LocalAppData%\ContextRelay\shared\` on Windows (macOS/Linux paths in the VS Code side only).
- Files: `snippets.json`, `chat-history.json`, `handoff-index.json`, `schema.json`.
- Writes use atomic rename + OS file lock. Reads are non-blocking; corrupt files are quarantined to `.bak.<timestamp>`.
- `FileSystemWatcher` (VS) / `fs.watch` (VS Code) pushes changes across processes with a 200 ms debounce.
- Conflict policy: last-writer-wins with `id` + `updatedAt`. Deletes are tombstoned for 7 days.
- VS Code side migration: on first run after adopting the shared store, existing `workspaceState` snippets are migrated once and the migration flag is persisted.
- Every shared-store mutation also refreshes `schema.json`, and writers preserve unknown top-level fields so future additive schema changes are not stripped accidentally.
- `handoff-index.json` values are canonicalized on write: `workspaceRoot` is normalized to native path form, and document paths use `/` separators.
- Schema version 2 adds the `sharepoint-embedded` snippet source. The version is shared by every store file, so version 1 files are quarantined and reset once; there is no migration. Readers map `sharepoint-embedded` explicitly instead of parsing enum names. See [ADR 0002](adr/0002-shared-store-schema-v2.md).
- The shared store and `settings.json` are shared by the Experimental Instance and regular Visual Studio regardless of root suffix, so validation runs with the released extension disabled.

## 12. Logging

- Output window panes: `ContextRelay`, `ContextRelay Debug`.
- `EnableGraphDebugLogging` enables request/response summaries (PII masked).
- Unhandled exceptions → `ActivityLog` (Warning+) and user-facing `InfoBar`.

## 13. Packaging / CI

- VSIX targeting Visual Studio 17.9+ with installable manifest entries for Community/Pro/Enterprise on both amd64 and arm64, plus pkgdef generation, compiled VSCT menu resources, and local `LICENSE.txt` packaging.
- SDK-style VSIX packaging is enabled through `Microsoft.VsSDK.targets` imported via `CustomAfterMicrosoftCSharpTargets`, with `GeneratePkgDefFile=true` and CLI deployment disabled.
- GitHub Actions (`windows-latest`) restores, runs the vulnerable/deprecated package audit, builds the solution, runs Core tests, and uploads the generated `.vsix` artifact.
- The `vsix-skeleton` milestone is complete once the `.vsix` artifact is emitted by CLI build and the remaining work is only manual host validation.
- Development-only SharePoint Embedded MCP: the official SPE MCP server runs at a pinned version with `--read-only --tools readOnly` from `.vscode/mcp.json` (VS Code and Visual Studio), `.mcp.json` plus a launcher script (Claude Code and Copilot CLI, with separate state directories) and `.codex/config.toml` (Codex). It needs Node.js, is excluded from the VSIX and is never a runtime dependency. Visual Studio also reads the repository `.mcp.json` and `.vscode/mcp.json`; duplicate registration, `~/` expansion of the state directory and the launcher's client detection are validated, and a generated untracked `.vs/mcp.json` replaces them for Visual Studio if they misbehave. `.gitignore` guards against state directories created inside the repository.

## 14. Testing

- xUnit for Core. WPF ViewModels are pure POCOs and unit-tested.
- Manual E2E checklist lives in `docs/e2e_checklist.md` and is intended for `/rootsuffix Exp` on VS 2022 and VS 2026.
- Shared-store tests explicitly cover `schema.json` emission, forward-compatible unknown-field preservation, path canonicalization, and corrupt-file quarantine behavior.

## 15. Risks

1. WAM unavailable on non-domain machines — provide an MSAL Embedded/WebView2 fallback path.
2. `/beta` Graph APIs (Retrieval / Chat) may change — gate under a feature flag and surface clear errors.
3. Copilot-for-VS has no public API for direct prompt injection — soft handoff via clipboard + command invocation.
4. Shared-store concurrency with the VS Code extension — test carefully with both running simultaneously.
5. A buildable assembly is not sufficient for release readiness; CI must prove that the VSIX artifact, pkgdef/menu registration, and `/rootsuffix Exp` load path all work on supported VS versions.
6. The SharePoint Embedded data source and its pay-as-you-go billing are in preview and depend on tenant prerequisites outside this extension (owning-app grant, discoverability, billing, semantic index). The feature is opt-in, Global only, and states preview and billing in the UI and documents; tenant behavior stays unverified until checked in an approved environment.

## 16. Todo tracking

Initial todos are seeded in the session SQL store. Update this plan whenever high-level design decisions change; keep low-level work items in SQL.

## 17. GitHub issue breakdown

- **#10** Implement Core authentication stack for ContextRelayVS
  - **#1** auth scope catalog and options model
  - **#2** MSAL.NET + WAM auth provider
  - **#3** DPAPI / MsalCacheHelper token cache
- **#4** slash command router
- **#5** shared snippet repository integration
- **#6** handoff document generator
- **#7** TTL + LRU cache service
- **#8** VS Code extension shared-store migration
- **#11** finish VSIX packaging and install validation
  - **#9** installable VSIX assets + Experimental Instance validation
- **#223** SharePoint Embedded retrieval and tool window improvements
  - **#224** design documents
  - **#225** retrieval, authentication and data
  - **#226** tool window UI and Markdown
  - **#227** Options
  - **#228** development MCP, setup documents and end-to-end validation

## 18. Current implementation status

- **Implemented end-to-end in-repo**: shared session store, schema docs, MSAL auth core, slash-command router, shared snippet repository, handoff document generator, TTL + LRU cache, Graph/retrieval/chat adapters, localized WPF tool window UI, slash-command popup, result-card context actions, dedicated `/connectors` routing, `/ask` editor previews, VS commands, options pages, logging panes, and installable VSIX packaging.
- **Parity follow-up against the VS Code extension**: result pinning now toggles/unpins instead of only warning on duplicates, mail/SharePoint/OneDrive pinning hydrates fuller content for handoff use, `/ask` now uses the shared explicit-context and attachment rules described in [Issue #184](https://github.com/kkamegawa/ContextRelayVS/issues/184), and the soft handoff command now tries to open GitHub Copilot Chat after copying the prompt.
- **Shared-store behavior covered**: schema emission, unknown-field preservation, handoff path normalization, tombstone-aware snippet merge, retry on Windows atomic replace failures.
- **Repository readiness improved**: README / README_ja now reflect implementation status, `docs/e2e_checklist.md` and `docs/marketplace_release.md` exist, CI audits vulnerable/deprecated packages, and release assets include a Marketplace publish manifest plus a release workflow.
- **Still missing for release readiness**: `/rootsuffix Exp` validation on supported VS versions and the out-of-repo VS Code shared-store migration PR.
- **Intentional remaining VS/VS Code difference**: GitHub Copilot Chat prompt prefill remains clipboard-based in Visual Studio because this extension does not have a supported prompt-injection API comparable to VS Code's `workbench.action.chat.open`.
