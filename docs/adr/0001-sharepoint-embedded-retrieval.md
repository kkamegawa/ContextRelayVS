# ADR 0001: Retrieve configured SharePoint Embedded content with a dedicated token

[日本語](0001-sharepoint-embedded-retrieval_ja.md)

## Record Metadata

- Status: accepted
- Owner: unknown
- Created: 2026-10-25
- Last updated: 2026-10-25
- Documentation approval: approved (2026-10-25, user approval at the #224 design gate)
- Tracking: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223), [Issue #225](https://github.com/kkamegawa/ContextRelayVS/issues/225)
- Related records: [Design plan §6.1](../plan.md#61-sharepoint-embedded-retrieval), [ADR 0002](0002-shared-store-schema-v2.md), [ADR 0003](0003-single-column-tool-window-and-markdown.md), [ADR 0004](0004-shared-settings-updates-and-embedded-options.md)

## Context and Requirements

- The VS Code extension added opt-in SharePoint Embedded retrieval in [kkamegawa/ContextRelay#287](https://github.com/kkamegawa/ContextRelay/issues/287). The Visual Studio extension must offer the same behavior.
- Verified implementation today:
  - SharePoint and OneDrive use Graph Search.
  - Connectors use the Copilot Retrieval API through `RetrievalSearchAdapter`.
  - All Graph sources share one token whose scopes come from `AuthScopeCatalog.BuildRequiredScopes`.
  - Work IQ already uses its own token through `MsalAuthProvider.GetWorkIqAccessTokenAsync`.
- SharePoint Embedded content needs these layers (S1, S3):
  - the delegated Graph permission `FileStorageContainer.Selected`;
  - a container type application grant that only the owning application can register;
  - the user's own container access;
  - content discoverability, pay-as-you-go billing and an initialized semantic index.
- The Retrieval API itself is generally available, but the `sharePointEmbedded` data source and its billing are in preview (S1, S2). The API is offered only in the Global cloud (S4).
- Upstream first added `FileStorageContainer.Selected` to its shared session. Missing Embedded consent then broke sign-in for every source, and upstream moved Embedded to a dedicated session (S5).

## Alternatives

| Option | Requirement fit | Benefits | Costs / risks | Evidence |
|--------|-----------------|----------|---------------|----------|
| Direct Retrieval with a dedicated Embedded token | Full | Missing consent affects only `/embedded`; reuses the Work IQ token pattern | A second consent prompt on the first `/embedded` | S1, S5 |
| Direct Retrieval with the scope added to the shared token | Partial | One consent prompt | Missing consent blocks every source, as upstream observed | S5 |
| Graph Search scoped by container type | Partial | No preview billing | Returns hits, not grounding extracts; differs from upstream | S1 |
| Read-only SPE MCP at runtime | Poor | No adapter code | Adds a Node.js runtime dependency and leaves the product boundary | S5 |

## Current Decision

- Add an opt-in `/embedded <natural-language query>` command. It is excluded from `/all` and never falls back to another source.
- Embedded is active only when `EmbeddedEnabled` is true, `CloudEnvironment` is Global and at least one valid enabled source exists. Each source has a friendly `Name`, a `ContainerTypeId` and `Enabled`.
- Validate activation, source selection and a trimmed 1-1500 character query before authentication. Clamp the result count to 1-25.
- Acquire a dedicated token for delegated `FileStorageContainer.Selected` through `GetEmbeddedAccessTokenAsync`. Never add the scope to `BuildRequiredScopes`. Consent failures name the scope and point to the owning-app grant and the app registration.
- Do not add `Files.Read.All` or `Sites.Read.All` for Embedded-only use unless a tenant check proves they are required.
- Call `POST /v1.0/copilot/retrieval` with `dataSource: sharePointEmbedded`, the selected `containerTypeId`, `resourceMetadata: ["title"]` and `maximumNumberOfResults`. Share the transport and extract conversion with connectors.
- Map 400, 401, 403, 404 and 429 to specific guidance. Report zero results only for a 200 response with empty `retrievalHits`.
- Keep a memory-only Embedded cache isolated by account, cloud and endpoint, container type, result limit and query. It is never written to the workspace cache snapshot.
- Discard cached and displayed Embedded results when the account, authentication or Embedded settings change. A per-request generation number drops stale responses, and a request invalidated by a settings change reports a message instead of disappearing silently.
- Pinned Embedded extracts go to chat `additionalContext` within the existing budget, to Ask prompts and to handoff documents. They are never sent to Work IQ and never converted to file references.

## Consequences and Review Triggers

- Users see a second consent prompt the first time they run `/embedded`. Other sources keep working when that consent is missing.
- Preview status and pay-as-you-go billing must be visible in the UI and documentation.
- Settings identify the target; they do not grant permissions. Microsoft-owned Loop containers are not promised to work.
- Confidence is medium: request shape, permissions and cloud availability come from current documentation, but live tenant behavior is unverified until checked in an approved environment.
- Reconsider when the data source reaches general availability, when Graph Search can return extracts, or when a tenant check shows extra scopes are required.

## Sources

| ID | Title | Exact URL | Checked on | Relevant claim / section | Relevance to this decision |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | Add Microsoft 365 Copilot and agent experiences | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/agent-experiences | 2026-10-25 | Use the Retrieval API: request body, `FileStorageContainer.Selected`, preview, prerequisites, billing, discoverability | Defines the request and prerequisites |
| S2 | Billing meters | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/reference/billing-meters | 2026-10-25 | The `sharePointEmbedded` data source and its billing are in preview; the Retrieval API is GA | Preview and billing notice |
| S3 | Plan authentication and permissions | https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/authentication-permissions | 2026-10-25 | Delegated `FileStorageContainer.Selected` needs no admin consent; container type grants are required | Token and grant design |
| S4 | Retrieve grounding data | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/retrieval/copilotroot-retrieval | 2026-10-25 | National cloud table: Global only | Activation requires the Global cloud |
| S5 | ContextRelay ADR 0001 | https://github.com/kkamegawa/ContextRelay/blob/main/docs/adr/0001-sharepoint-embedded-retrieval.md | 2026-10-25 | Change history: the shared-session scope broke every adapter; Embedded moved to a dedicated session | Reason for the dedicated token |

## Change History

This record uses approved in-place updates rather than append-only replacement ADRs. Preserve all history rows.

| Date | Previous decision summary | New decision summary | Reason | Source IDs | Approval reference |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | Initial proposal | Direct opt-in Embedded retrieval with a dedicated token | Port of upstream #287; the dedicated token replaces the shared-scope design in the first approved spec | S1-S5 | User approval, 2026-10-25 (#224) |
