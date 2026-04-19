# ContextRelay for Visual Studio

ContextRelay for Visual Studio is a Visual Studio (2022 / 2026) extension that surfaces relevant Microsoft 365 context (Exchange Mail, Microsoft Teams, SharePoint, OneDrive) in a tool window while you design and code. It ports the feature set of the VS Code extension [ContextRelay](https://github.com/kkamegawa/ContextRelay) to the Visual Studio platform, and can share pinned snippets, chat history, and handoff-document pointers with the VS Code version on the same machine.

> **Status**: Design / scaffolding phase. See [docs/plan.md](docs/plan.md) for the full design document and the implementation roadmap. The VSIX is not yet shipped.

## Features (planned, mirroring the VS Code version)

- **Keyword-first search** across Exchange Mail, Teams, SharePoint, and OneDrive via Microsoft Graph.
- **Slash-command source targeting** — `/mail`, `/teams`, `/sharepoint`, `/onedrive`, `/all`, `/ask`, `/clear`.
- **Snippet pinning** — persist results as named snippets.
- **Timestamped handoff documents** — generate `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, and optional `HANDOFF.md` for GitHub Copilot handoff.
- **Cross-editor session sharing** — snippets, chat history, and handoff pointers are shared with the VS Code extension via `%LocalAppData%\ContextRelay\shared\`. See [docs/shared-session-schema.md](docs/shared-session-schema.md).

## Requirements (planned)

- Visual Studio 2022 17.8 or later, or Visual Studio 2026 (including Insider).
- .NET Framework 4.7.2 runtime (bundled with Visual Studio).
- A Microsoft 365 work/school account (Microsoft Entra ID). Personal Microsoft accounts are not supported.
- Microsoft Entra app registration with public-client flow enabled and delegated Microsoft Graph permissions (same scope set as the VS Code version).

## Architecture

| Layer | Project | Framework |
|---|---|---|
| VSIX / ToolWindow / Commands / Options | `src/ContextRelay.VSExtension` | .NET Framework 4.7.2 |
| Business logic (adapters, router, cache, snippets, handoff, shared store, auth) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF views & view-models (MVVM) | `src/ContextRelay.UI` | .NET Framework 4.7.2 |
| Unit tests | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

Authentication uses **MSAL.NET** (`Microsoft.Identity.Client`) with the Windows Account Manager (WAM) broker. Tokens are cached with DPAPI-encrypted `MsalCacheHelper`.

UI is native WPF bound to `VsBrushes` / `EnvironmentColors` so it follows the VS theme (Dark / Light / Blue) automatically.

## License

MIT. See [LICENSE](LICENSE).

## Related

- VS Code extension (upstream): <https://github.com/kkamegawa/ContextRelay>
- Design plan: [docs/plan.md](docs/plan.md)
- Shared-session schema: [docs/shared-session-schema.md](docs/shared-session-schema.md)
