# ContextRelay for Visual Studio

[![CI](https://github.com/kkamegawa/ContextRelayVS/actions/workflows/ci.yml/badge.svg)](https://github.com/kkamegawa/ContextRelayVS/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/kkamegawa/ContextRelayVS/branch/main/graph/badge.svg)](https://codecov.io/gh/kkamegawa/ContextRelayVS)

ContextRelay for Visual Studio is a Visual Studio (2022 / 2026) extension that surfaces relevant Microsoft 365 context (Exchange Mail, Microsoft Teams, SharePoint, OneDrive, OneNote, Planner, and Microsoft To Do) in a tool window while you design and code. It ports the feature set of the VS Code extension [ContextRelay](https://github.com/kkamegawa/ContextRelay) to the Visual Studio platform, and can share pinned snippets, chat history, and handoff-document pointers with the VS Code version on the same machine.

> **Status**: Implemented preview. The repository builds an installable VSIX locally and now includes the planned in-repo UX features: localized tool-window text, slash-command discovery, result actions, `/connectors`, plain Microsoft 365 Copilot chat, `/ask` context chat, and `/workiq`. Manual Experimental Instance validation is still required before marketplace release.

## Implemented features

- **Plain Copilot chat** — input without a slash command starts or continues a Microsoft 365 Copilot conversation. Individual search results are never attached; the explicit context is pinned snippets, queued file attachments, `#file` mentions, and the saved active editor when that option is enabled. The latest ContextRelay search summary may still be sent as orientation, but it does not count as explicit context. Unlike `/ask`, plain chat also runs when no explicit context is available.
- **Explicit source search** across Exchange Mail, Teams, SharePoint, OneDrive, OneNote, Planner/To Do, and connectors via Microsoft Graph slash commands.
- **Slash-command source targeting** — `/mail`, `/teams`, `/sharepoint`, `/onedrive`, `/onenote`, `/task`, `/connectors`, `/all`, `/ask`, `/workiq`, `/clear`.
- **Slash-command discovery popup** — keyboard-navigable suggestions appear as you type `/...`.
- **Snippet pinning** — persist results as named snippets in the cross-editor shared store.
- **Shared chat history** — append/search history shared with the VS Code extension.
- **Timestamped handoff documents** — generate `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, and optional `HANDOFF.md` for GitHub Copilot handoff.
- **Soft Copilot handoff** — copy a generated prompt to the clipboard, append selected results to `HANDOFF.md`, and open GitHub Copilot Chat in Visual Studio when the command is available.
- **Copilot reply actions** — Copilot answers remain visible in the tool window with explicit Copy, Append to active editor, and Replace selection/document actions.
- **`/ask` context chat** — follows the explicit-context and attachment rules in [Issue #184](https://github.com/kkamegawa/ContextRelayVS/issues/184): pinned snippets, pending local attachments, and the eligible saved active editor (when enabled) are bounded and sent as context; the command is rejected when no usable context is available.
- **`/workiq` natural language work intelligence** — sends A2A v1.0 queries to the Work IQ Gateway with a dedicated token audience, keeps a separate Work IQ conversation context, and resets that context on `/clear`.
- **Local `#file` context** — mention workspace files such as `#README.md` or `#"docs/design notes.md"` in plain chat, `/ask`, or `/workiq` prompts. Copilot chat receives explicit file context; Work IQ receives bounded file text only when the Work IQ local-file opt-in is enabled.
- **Localized WPF tool window UI** — English/Japanese labels, status/help text, result-card context actions, and debug-log access.
- **JSON-backed settings persistence** — ContextRelay reads and writes its shared settings from `%AppData%\ContextRelay\settings.json`.
- **Chat settings** — Tools > Options > ContextRelay > General exposes a non-negative maximum attached-file count (default `5`), active-editor attachment (default off), and streamed responses (default on). Existing settings files remain valid because missing properties use these defaults.
- **MSAL.NET + WAM authentication** with DPAPI-backed token cache.
- **TTL + LRU cache** with workspace persistence.
- **Cross-editor session sharing** — snippets, chat history, and handoff pointers are shared with the VS Code extension via `%LocalAppData%\ContextRelay\shared\`. See [docs/shared-session-schema.md](docs/shared-session-schema.md).

## Slash commands and sources

| Command | Source |
|---|---|
| `/mail <query>` | Exchange Online mail |
| `/teams <query>` | Microsoft Teams messages |
| `/sharepoint <query>` | SharePoint sites and pages |
| `/onedrive <query>` | OneDrive files |
| `/onenote <query>` | OneNote pages |
| `/task <query>` | Planner tasks and Microsoft To Do tasks |
| `/connectors <query>` | Microsoft Graph connectors |
| `/all <query>` | All enabled sources |
| `/ask <instruction>` | Send explicit context (pinned snippets, queued attachments, `#file` mentions, active editor) to Microsoft 365 Copilot and show the answer in the panel; rejected when no explicit context is available |
| `/workiq <query>` | Send a natural language query to Work IQ (A2A protocol) |
| `/clear` | Clear chat transcript, pinned snippets, and Work IQ conversation context |

Local file mentions are supported in plain chat, `/ask`, and `/workiq`:

```text
Summarize #README.md
/ask #"docs/design notes.md" summarize the implementation risks
/workiq #docs/plan.md find related workplace context
```

File mentions are resolved only inside the opened Visual Studio workspace and restricted to Copilot-supported text/code file types. **Maximum attached files** (default five) applies to plain chat and `/ask`; `/workiq` always accepts up to five unique `#file` mentions. Work IQ local file context is disabled by default; enable **Allow local file context for Work IQ** in Tools > Options > ContextRelay before sending local file text to Work IQ.

## Chat context, attachments, and streaming

Plain chat and `/ask` use the same explicit context rules. `/ask` is rejected locally — before authentication and before any Copilot request — when none of that context is available, while plain chat runs either way. The latest ContextRelay search summary is sent as orientation when one exists, but it is not explicit context: it neither satisfies the `/ask` check nor changes web context.

- **Attachments** — queue workspace files with the **+** button in the tool window or **Attach File to Chat** on the Tools > ContextRelay menu, and drop one with **Remove** on its chip. `#file` mentions are attached first, then queued attachments, then the active editor, up to **Maximum attached files**; `0` disables attachments entirely. A request claims the attachments it sends and clears them from the queue, so files queued while a response is generating are kept for the next request.
- **Active editor** — enabling **Attach active editor** attaches the saved content of the file in the active editor. A selection narrows the attachment to the selected lines, except when the buffer has unsaved edits: the saved file is attached whole, because the live line numbers no longer match the file on disk.
- **Grounding** — a request that carries explicit context also carries an instruction to treat the attached files and pinned snippets as the primary sources, and Copilot web context is disabled for that request. A request whose only context is the search summary is sent unchanged.
- **Streaming** — with **Stream chat responses** enabled, the reply is shown incrementally while it arrives and **Stop** cancels generation. Stopping never issues an automatic continuation request.

Responses that appear incomplete are reported as such and can be extended with the **Fetch continuation** button. Continuation is always a manual action.

## Authentication and delegated permissions

ContextRelay uses Microsoft Entra delegated permissions through the signed-in Visual Studio user. Typical minimum sets by scenario:

| Scenario | Minimum delegated permissions |
|---|---|
| Exchange mail search | `User.Read`, `Mail.Read` |
| Teams message search | `User.Read`, `Chat.Read`, `ChannelMessage.Read.All` |
| SharePoint and OneDrive search | `User.Read`, `Files.Read.All`, `Sites.Read.All` |
| OneNote search | `User.Read`, `Notes.Read` |
| Planner and To Do search (`/task`) | `User.Read`, `Tasks.Read` |
| Connectors search | `User.Read`, `ExternalItem.Read.All` |

> Notes:
>
> - Admin consent is commonly required for `ChannelMessage.Read.All` and `ExternalItem.Read.All`.
> - `/workiq` uses the non-Graph delegated permission `WorkIQAgent.Ask` on `api://workiq.svc.cloud.microsoft`.
> - Personal Microsoft accounts are not supported.

## Security model

ContextRelay applies the same security-first posture used in the VS Code extension:

- External links are normalized through an allowlist (`https`, `http`, `mailto`) before launch.
- Unsafe or malformed URLs are dropped instead of being forwarded to host navigation.
- Adapter text normalization strips script/style payloads from HTML-derived snippet content.
- Plain chat and `/ask` keep source attachment explicit to reduce accidental over-sharing of context.
- `/workiq #file` prompts require an explicit option before local file text is sent to the Work IQ service.

## Build and package

- Visual Studio 2022 17.8 or later, or Visual Studio 2026 (including Insider).
- .NET Framework 4.8 runtime (bundled with Visual Studio).
- A Microsoft 365 work/school account (Microsoft Entra ID). Personal Microsoft accounts are not supported.
- Microsoft Entra app registration with public-client flow enabled, delegated Microsoft Graph permissions, and optional `WorkIQAgent.Ask` consent for `/workiq`. See [docs/tenant_admin_quickstart.md](docs/tenant_admin_quickstart.md).

```powershell
pwsh -File build\Invoke-PackageAudit.ps1 -SolutionPath .\ContextRelayVS.sln
dotnet build ContextRelayVS.sln -v minimal
dotnet test tests\ContextRelay.Core.Tests\ContextRelay.Core.Tests.csproj -v minimal
```

## Usage quick start

Plain text input starts or continues Microsoft 365 Copilot chat in the tool window without implicit source search.

To search specific sources, use slash commands:

```text
/all architecture decision
/onenote release checklist
/task onboarding
/ask summarize pinned snippets as release notes
```

The VSIX is emitted at:

```text
src\ContextRelay.VSExtension\bin\<Configuration>\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix
```

## Manual validation

- Use the checklist in [docs/e2e_checklist.md](docs/e2e_checklist.md).
- Use the Marketplace/release guide in [docs/marketplace_release.md](docs/marketplace_release.md) when preparing a publishable VSIX.
- Validate install/load under a Visual Studio Experimental Instance (`/rootsuffix Exp`) before publishing.

## Architecture

| Layer | Project | Framework |
|---|---|---|
| VSIX / ToolWindow / Commands / Options | `src/ContextRelay.VSExtension` | net8.0-windows10.0.22621.0 |
| Business logic (adapters, router, cache, snippets, handoff, shared store, auth) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF views & view-models (MVVM) | `src/ContextRelay.UI` | net8.0-windows |
| Unit tests | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

Authentication uses **MSAL.NET** (`Microsoft.Identity.Client`) with the Windows Account Manager (WAM) broker. Tokens are cached with DPAPI-encrypted `MsalCacheHelper`.

UI is native WPF bound to `VsBrushes` / `EnvironmentColors` so it follows the VS theme (Dark / Light / Blue) automatically.

## Current known gaps

- Experimental Instance behavior still needs host-side manual validation.
- Visual Studio **Tools > Options** integration is deployed as a sidecar in-proc package and registered in the Experimental Instance hive during Debug builds.
- Marketplace publishing still requires PAT provisioning and a manual release trigger.
- The VS Code repository still needs its separate shared-store migration PR.
- Prompt injection into GitHub Copilot Chat is still clipboard-based in Visual Studio; unlike VS Code, there is no supported prompt-prefill API wired into this extension.

## Work IQ

`/workiq` sends natural language queries to the Work IQ Gateway over the A2A (Agent-to-Agent) v1.0 protocol:

- Endpoint: `https://workiq.svc.cloud.microsoft/a2a/`
- Delegated permission: `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`
- Prerequisites: Microsoft 365 Copilot license, tenant admin consent, and Work IQ service-principal provisioning

Use `/workiq` for questions such as:

```text
/workiq Summarize my recent emails from Alice
/workiq What meetings do I have today?
/workiq Find documents about the Q3 budget review
```

Consecutive `/workiq` turns reuse the returned Work IQ `contextId`. `/clear` resets both the Microsoft 365 Copilot conversation and the Work IQ conversation state. See [docs/work_iq.md](docs/work_iq.md) for setup details.

## License

MIT. See [LICENSE](LICENSE).

## Related

- VS Code extension (upstream): <https://github.com/kkamegawa/ContextRelay>
- Design plan: [docs/plan.md](docs/plan.md)
- Marketplace/release guide: [docs/marketplace_release.md](docs/marketplace_release.md)
- Tenant admin quickstart: [docs/tenant_admin_quickstart.md](docs/tenant_admin_quickstart.md)
- Work IQ setup: [docs/work_iq.md](docs/work_iq.md)
- Shared-session schema: [docs/shared-session-schema.md](docs/shared-session-schema.md)
