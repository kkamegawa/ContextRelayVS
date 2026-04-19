# ContextRelay for Visual Studio

ContextRelay for Visual Studio is a Visual Studio (2022 / 2026) extension that surfaces relevant Microsoft 365 context (Exchange Mail, Microsoft Teams, SharePoint, OneDrive) in a tool window while you design and code. It ports the feature set of the VS Code extension [ContextRelay](https://github.com/kkamegawa/ContextRelay) to the Visual Studio platform, and can share pinned snippets, chat history, and handoff-document pointers with the VS Code version on the same machine.

> **Status**: Implemented preview. The repository builds an installable VSIX locally and includes the core feature set, shared-store schema, and CI packaging flow. Manual Experimental Instance validation is still required before marketplace release.

## Implemented features

- **Keyword-first search** across Exchange Mail, Teams, SharePoint, and OneDrive via Microsoft Graph.
- **Slash-command source targeting** — `/mail`, `/teams`, `/sharepoint`, `/onedrive`, `/all`, `/ask`, `/clear`.
- **Snippet pinning** — persist results as named snippets in the cross-editor shared store.
- **Shared chat history** — append/search history shared with the VS Code extension.
- **Timestamped handoff documents** — generate `PLAN.md`, `TASKS.md`, `TEST_PLAN.md`, and optional `HANDOFF.md` for GitHub Copilot handoff.
- **Soft Copilot handoff** — copy a generated prompt to the clipboard and open the relevant VS surface.
- **WPF tool window UI** — search box, results list, snippets, history, actions, and debug-log access.
- **DialogPage options** — General, Authentication, Cache, and Adapters settings pages.
- **MSAL.NET + WAM authentication** with DPAPI-backed token cache.
- **TTL + LRU cache** with workspace persistence.
- **Cross-editor session sharing** — snippets, chat history, and handoff pointers are shared with the VS Code extension via `%LocalAppData%\ContextRelay\shared\`. See [docs/shared-session-schema.md](docs/shared-session-schema.md).

## Build and package

- Visual Studio 2022 17.8 or later, or Visual Studio 2026 (including Insider).
- .NET Framework 4.7.2 runtime (bundled with Visual Studio).
- A Microsoft 365 work/school account (Microsoft Entra ID). Personal Microsoft accounts are not supported.
- Microsoft Entra app registration with public-client flow enabled and delegated Microsoft Graph permissions (same scope set as the VS Code version).

```powershell
dotnet build ContextRelayVS.sln -v minimal
dotnet test tests\ContextRelay.Core.Tests\ContextRelay.Core.Tests.csproj -v minimal
```

The VSIX is emitted at:

```text
src\ContextRelay.VSExtension\bin\<Configuration>\net472\ContextRelay.VSExtension.vsix
```

## Manual validation

- Use the checklist in [docs/e2e_checklist.md](docs/e2e_checklist.md).
- Validate install/load under a Visual Studio Experimental Instance (`/rootsuffix Exp`) before publishing.

## Architecture

| Layer | Project | Framework |
|---|---|---|
| VSIX / ToolWindow / Commands / Options | `src/ContextRelay.VSExtension` | .NET Framework 4.7.2 |
| Business logic (adapters, router, cache, snippets, handoff, shared store, auth) | `src/ContextRelay.Core` | netstandard2.0 |
| WPF views & view-models (MVVM) | `src/ContextRelay.UI` | .NET Framework 4.7.2 |
| Unit tests | `tests/ContextRelay.Core.Tests` | net8.0 (xUnit) |

Authentication uses **MSAL.NET** (`Microsoft.Identity.Client`) with the Windows Account Manager (WAM) broker. Tokens are cached with DPAPI-encrypted `MsalCacheHelper`.

UI is native WPF bound to `VsBrushes` / `EnvironmentColors` so it follows the VS theme (Dark / Light / Blue) automatically.

## Current known gaps

- Marketplace publishing is not wired yet; CI builds artifacts only.
- Experimental Instance behavior still needs host-side manual validation.
- The VS Code repository still needs its separate shared-store migration PR.

## License

MIT. See [LICENSE](LICENSE).

## Related

- VS Code extension (upstream): <https://github.com/kkamegawa/ContextRelay>
- Design plan: [docs/plan.md](docs/plan.md)
- Shared-session schema: [docs/shared-session-schema.md](docs/shared-session-schema.md)
