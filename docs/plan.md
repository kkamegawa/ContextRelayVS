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
| Keyword / slash-command search (`/mail` `/teams` `/sharepoint` `/onedrive` `/all` `/ask` `/clear`) | Ported 1:1 (same grammar) |
| Snippet pinning (persisted across sessions) | Cross-editor shared store (`%LocalAppData%\ContextRelay\shared\snippets.json`) |
| Handoff docs (`PLAN.md` / `TASKS.md` / `TEST_PLAN.md` / `HANDOFF.md`) | Written under solution root `.contextrelay/` (fallback `%USERPROFILE%\.contextrelay\`) |
| Chat tab (Copilot Chat API /beta) | Phase 2 (feature flag off in Phase 1) |
| Cache (TTL + LRU) | Re-implemented in C# |
| Debug output | VS output window pane "ContextRelay Debug" |

### Phasing

- **Phase 0**: Repo/solution scaffold, MIT LICENSE, CI, empty VSIX launches.
- **Phase 1 (MVP)**: Auth + Mail/SharePoint/OneDrive/Teams search + slash router + snippet pinning + handoff docs + settings + cache + debug log + cross-editor shared store.
- **Phase 2**: Copilot Chat API (`/ask`), connectors, deeper Copilot-for-VS handoff, Japanese UI.
- **Phase 3**: Marketplace publish, auto-update, opt-in telemetry.

## 2. Target environment

- Visual Studio 2022 (17.8+) and Visual Studio 2026 / Insider.
- .NET Framework 4.7.2 for the VSIX process; netstandard2.0 for Core.
- `AsyncPackage` for deferred load.
- Microsoft 365 work/school accounts only (Entra ID).

## 3. Project layout

```
ContextRelayVS/
├─ src/
│  ├─ ContextRelay.VSExtension/   # VSIX, AsyncPackage, ToolWindow, commands, options
│  ├─ ContextRelay.Core/          # netstandard2.0; adapters, auth, cache, router, snippets, handoff, shared store
│  └─ ContextRelay.UI/            # WPF MVVM
├─ tests/
│  └─ ContextRelay.Core.Tests/    # xUnit
├─ docs/                          # plan.md, shared-session-schema.md, tenant admin guide, etc.
├─ LICENSE                        # MIT
├─ README.md / README_ja.md
└─ ContextRelayVS.sln
```

## 4. UI

- One `ToolWindowPane` hosting a WPF `UserControl` via MVVM.
- Bound to `VsBrushes` / `EnvironmentColors` for automatic theme follow.
- Single input with slash-command popup (keyboard-navigable `ListBox` in `Popup`).
- Results rendered as source sections (Mail / Teams / SharePoint / OneDrive / Connectors) with `DataTemplate` per type.
- Card context menu: Pin, Copy, Append to handoff, Open in browser.
- VS Code-specific sidebar move commands are dropped; VS native dock/float/tab is used instead.

## 5. Auth

- `Microsoft.Identity.Client` + `Microsoft.Identity.Client.Broker` (WAM).
- Interactive (WAM) → Silent (cache) → Interactive fallback.
- Token cache under `%LocalAppData%\ContextRelayVS\msal.cache` via `MsalCacheHelper` + DPAPI.
- Same Graph scope set as the VS Code version.
- Entra app-registration requirements documented in `docs/tenant_admin_quickstart.md` (TBD).

## 6. Graph calls

- `Microsoft.Graph` v5 for REST SDK. `Microsoft.Graph.Beta` for `/beta` (Chat, Retrieval where needed).
- Retrieval API (`POST /v1.0/copilot/retrieval`) and Chat API (`/beta/copilot/conversations`) called directly via `HttpClient`.

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

Persisted via `SVsSettingsManager` / `WritableSettingsStore` (collection `ContextRelay`). Keys map 1:1 to VS Code settings (documented cross-reference table in README).

- **General**: `MaxResults`, `OutputDir`, `EnableChatPreview`, `EnableGraphDebugLogging`
- **Authentication**: `Auth.ClientId`, `Auth.TenantId`
- **Cache**: `Cache.TtlSeconds`, `Cache.MaxEntries`, `Cache.PersistWorkspaceState`
- **Adapters**: `Adapters.Mail`, `.Teams`, `.Sharepoint`, `.OneDrive`, `.Connectors`

## 9. Handoff docs

- Output under active solution `.contextrelay/` (fallback `%USERPROFILE%\.contextrelay\` when no solution is open).
- `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, optional `HANDOFF.md`. Timestamped appended sections, same format as VS Code version.
- `CopyHandoffPrompt` puts a Copilot-for-VS-ready prompt on the clipboard.

## 10. Cache and snippets

- `TtlLruCache<TKey, TValue>` — default TTL 300 s, max 200 entries, LRU eviction.
- Cache optional persistence to `.vs/ContextRelay/cache.json` (solution scope).
- **Snippets persisted to the cross-editor shared store** (see §11), not a private VS file.

## 11. Cross-editor session sharing

See [`shared-session-schema.md`](shared-session-schema.md) for the authoritative schema.

- Scope: **data only** (snippets, chat history, handoff-document pointers). No tokens, no raw Graph responses.
- Storage root: `%LocalAppData%\ContextRelay\shared\` on Windows (macOS/Linux paths in the VS Code side only).
- Files: `snippets.json`, `chat-history.json`, `handoff-index.json`, `schema.json`.
- Writes use atomic rename + OS file lock. Reads are non-blocking; corrupt files are quarantined to `.bak.<timestamp>`.
- `FileSystemWatcher` (VS) / `fs.watch` (VS Code) pushes changes across processes with a 200 ms debounce.
- Conflict policy: last-writer-wins with `id` + `updatedAt`. Deletes are tombstoned for 7 days.
- VS Code side migration: on first run after adopting the shared store, existing `globalState` snippets are migrated once and the migration flag is persisted.
- Every shared-store mutation also refreshes `schema.json`, and writers preserve unknown top-level fields so future additive schema changes are not stripped accidentally.
- `handoff-index.json` values are canonicalized on write: `workspaceRoot` is normalized to native path form, and document paths use `/` separators.

## 12. Logging

- Output window panes: `ContextRelay`, `ContextRelay Debug`.
- `EnableGraphDebugLogging` enables request/response summaries (PII masked).
- Unhandled exceptions → `ActivityLog` (Warning+) and user-facing `InfoBar`.

## 13. Packaging / CI

- VSIX targeting `[17.0,19.0)` with an installable manifest, pkgdef generation, compiled VSCT menu resources, and local `LICENSE.txt` packaging.
- SDK-style VSIX packaging is enabled through `Microsoft.VsSDK.targets` imported via `CustomAfterMicrosoftCSharpTargets`, with `GeneratePkgDefFile=true` and CLI deployment disabled.
- GitHub Actions (`windows-latest`) restores, runs the vulnerability audit, builds the solution, runs Core tests, and uploads the generated `.vsix` artifact.
- The `vsix-skeleton` milestone is complete once the `.vsix` artifact is emitted by CLI build and the remaining work is only manual host validation.

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

## 18. Current implementation status

- **Implemented end-to-end in-repo**: shared session store, schema docs, MSAL auth core, slash-command router, shared snippet repository, handoff document generator, TTL + LRU cache, Graph/retrieval/chat adapters, WPF tool window UI, VS commands, options pages, logging panes, and installable VSIX packaging.
- **Shared-store behavior covered**: schema emission, unknown-field preservation, handoff path normalization, tombstone-aware snippet merge, retry on Windows atomic replace failures.
- **Repository readiness improved**: README / README_ja now reflect implementation status, `docs/e2e_checklist.md` exists, and CI uploads a built VSIX artifact.
- **Still missing for release readiness**: `/rootsuffix Exp` validation on supported VS versions, marketplace publish/sign flow, tenant-admin quickstart polishing, and the out-of-repo VS Code shared-store migration PR.
