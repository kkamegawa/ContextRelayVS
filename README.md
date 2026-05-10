# ContextRelay for Visual Studio

[![CI](https://github.com/kkamegawa/ContextRelayVS/actions/workflows/ci.yml/badge.svg)](https://github.com/kkamegawa/ContextRelayVS/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/kkamegawa/ContextRelayVS/branch/main/graph/badge.svg)](https://codecov.io/gh/kkamegawa/ContextRelayVS)

ContextRelay for Visual Studio is a Visual Studio (2022 / 2026) extension that surfaces relevant Microsoft 365 context (Exchange Mail, Microsoft Teams, SharePoint, OneDrive, OneNote, Planner, and Microsoft To Do) in a tool window while you design and code. It ports the feature set of the VS Code extension [ContextRelay](https://github.com/kkamegawa/ContextRelay) to the Visual Studio platform, and can share pinned snippets, chat history, and handoff-document pointers with the VS Code version on the same machine.

> **Status**: Implemented preview. The repository builds an installable VSIX locally and now includes the planned in-repo UX features: localized tool-window text, slash-command discovery, result actions, `/connectors`, plain Microsoft 365 Copilot chat, `/ask` context chat, and `/workiq`. Manual Experimental Instance validation is still required before marketplace release.

## Implemented features

- **Plain Copilot chat** — input without a slash command starts or continues a Microsoft 365 Copilot conversation without implicit ContextRelay search context.
- **Explicit source search** across Exchange Mail, Teams, SharePoint, OneDrive, OneNote, Planner/To Do, and connectors via Microsoft Graph slash commands.
- **Slash-command source targeting** — `/mail`, `/teams`, `/sharepoint`, `/onedrive`, `/onenote`, `/task`, `/connectors`, `/all`, `/ask`, `/workiq`, `/clear`.
- **Slash-command discovery popup** — keyboard-navigable suggestions appear as you type `/...`.
- **Snippet pinning** — persist results as named snippets in the cross-editor shared store.
- **Shared chat history** — append/search history shared with the VS Code extension.
- **Timestamped handoff documents** — generate `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, and optional `HANDOFF.md` for GitHub Copilot handoff.
- **Soft Copilot handoff** — copy a generated prompt to the clipboard, append selected results to `HANDOFF.md`, and open GitHub Copilot Chat in Visual Studio when the command is available.
- **Copilot reply actions** — Copilot answers remain visible in the tool window with explicit Copy, Append to active editor, and Replace selection/document actions.
- **`/ask` context chat** — requires pinned snippets, caps the forwarded context, sends it to Microsoft 365 Copilot, and saves the reply to shared chat history without automatically editing or opening a document.
- **`/workiq` natural language work intelligence** — sends A2A v1.0 queries to the Work IQ Gateway with a dedicated token audience, keeps a separate Work IQ conversation context, and resets that context on `/clear`.
- **Localized WPF tool window UI** — English/Japanese labels, status/help text, result-card context actions, and debug-log access.
- **JSON-backed settings persistence** — ContextRelay reads and writes its shared settings from `%AppData%\ContextRelay\settings.json`.
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
| `/ask <instruction>` | Send pinned snippets to Microsoft 365 Copilot and show the answer in the panel |
| `/workiq <query>` | Send a natural language query to Work IQ (A2A protocol) |
| `/clear` | Clear chat transcript, pinned snippets, and Work IQ conversation context |

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
