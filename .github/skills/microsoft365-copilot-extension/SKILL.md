---
name: microsoft365-copilot-extension
description: Build or debug Microsoft 365 Copilot integrations in .NET or JavaScript. Use when agents need to implement Graph Copilot Chat or Work IQ flows, shape additionalContext and contextualResources payloads, normalize IANA time zones, handle licensing and admin consent, or troubleshoot Microsoft 365 Copilot extension failures.
---

# Microsoft 365 Copilot Extension Implementation

This skill captures the reusable implementation lessons from ContextRelayVS work on plain Microsoft 365 Copilot chat, Work IQ integration, file-context relay, and the `locationHint.timeZone` regression. Use it when building or debugging a client that talks directly to Microsoft 365 Copilot surfaces from **.NET** or **JavaScript**.

Primary workflow:
1. Identify the target surface first: **Graph Copilot Chat** (`/beta/copilot/conversations`) or **Work IQ A2A** (`https://workiq.svc.cloud.microsoft/a2a/`).
2. Verify tenant prerequisites before coding: supported Entra account type, Microsoft 365 Copilot license, app registration, delegated permissions, and admin consent.
3. Persist the server-issued conversation handle instead of replaying history: `conversationId` for Copilot Chat, `contextId` for Work IQ.
4. Shape context deliberately: text belongs in `additionalContext`; file references belong in `contextualResources.files` only when the API supports them.
5. Normalize time-zone metadata before sending it. **Do not send Windows time-zone IDs in `locationHint.timeZone`; send an IANA zone or `Etc/UTC`.**
6. Diagnose failures at the protocol boundary first: endpoint, headers, status code, error code, and request ID.

Use these sample files first:
- [dotnet-copilot-chat-request.cs](sample_codes/dotnet-copilot-chat-request.cs)
- [javascript-copilot-chat-request.mjs](sample_codes/javascript-copilot-chat-request.mjs)
- [dotnet-workiq-send-message.cs](sample_codes/dotnet-workiq-send-message.cs)
- [javascript-workiq-send-message.mjs](sample_codes/javascript-workiq-send-message.mjs)

## Repo Evidence First

Start from the repo-specific evidence before searching the wider web:

- **Issue #25 / PR #26** — plain Microsoft 365 Copilot chat mode, explicit context flow, and server-owned conversation state
- **Issue #64** — `#` file-context relay design for Copilot and Work IQ
- **`src\ContextRelay.Core\Adapters\CopilotChatAdapter.cs`** — Graph Copilot Chat request shape and `locationHint.timeZone`
- **`src\ContextRelay.Core\Chat\ChatContextPayloadBuilder.cs`** — bounded `additionalContext` and `contextualResources.files` routing
- **`src\ContextRelay.Core\Adapters\WorkIqAdapter.cs`** — A2A v1.0 envelope, `A2A-Version: 1.0`, retries, and location metadata
- **`docs\tenant_admin_quickstart.md`** and **`docs\work_iq.md`** — tenant setup, permission scope, and protocol notes
- **Recent timezone regression** — Graph Copilot Chat rejected Windows IDs such as `Tokyo Standard Time` with an `IANA format` error; the safe behavior is to send IANA when available, otherwise `Etc/UTC`

## Key Concepts

### 1) Copilot Chat and Work IQ are different integration surfaces

Do not collapse them into one abstraction too early.

- **Graph Copilot Chat** is a REST flow under Microsoft Graph beta. You create a conversation, then post chat messages to that conversation.
- **Work IQ** is an **A2A v1.0 JSON-RPC** flow over `https://workiq.svc.cloud.microsoft/a2a/` with `SendMessage` and optional `contextId` continuation.

Treat each surface's headers, request body, and error model independently.

### 2) Prerequisites fail more often than the code does

Most early failures come from tenant setup, not syntax:

- the signed-in user lacks a **Microsoft 365 Copilot license**
- the app registration is missing delegated permissions
- **admin consent** has not been granted
- the app is registered for the wrong account type
- Work IQ is missing the **`WorkIQAgent.Ask`** delegated permission or service principal provisioning

Confirm these before changing request-shaping logic.

### 3) Context must be typed and bounded

ContextRelay learned that not all context should become prompt text.

- Use **`additionalContext`** for bounded text summaries, pinned snippets, and search summaries.
- Use **`contextualResources.files`** for file-like resources that the Copilot surface can dereference directly.
- Keep a character budget for text context so requests remain predictable and debuggable.
- Do not send the same artifact as both a file resource and duplicated prompt text unless you have a specific reason.

### 4) Conversation state belongs to the service

Do not rebuild multi-turn behavior by resending the entire transcript.

- Persist **`conversationId`** for Graph Copilot Chat.
- Persist **`contextId`** for Work IQ.
- Reset those handles explicitly when the user clears state or switches scenario.

### 5) Time-zone format matters

This was a real regression in ContextRelayVS.

- **JavaScript** usually gives you an IANA identifier via `Intl.DateTimeFormat().resolvedOptions().timeZone`.
- **.NET on Windows** often gives a Windows time-zone ID such as `Tokyo Standard Time` from `TimeZoneInfo.Local.Id`.
- Graph Copilot Chat expects **IANA** in `locationHint.timeZone`. If you cannot map the local ID safely, send **`Etc/UTC`** instead of a Windows ID.
- Keep the rule simple in shared logic: **IANA or `Etc/UTC`; never Windows IDs.**

### 6) Troubleshoot from the wire format outward

When an integration fails, first confirm:

1. correct endpoint
2. correct auth audience and scopes
3. required headers
4. exact serialized payload
5. returned status code, request ID, and error body

Only after that should you change UI, routing, or higher-level abstractions.

## Common Patterns

### Pattern 1: Copilot Chat minimal flow

1. `POST /beta/copilot/conversations`
2. read the returned `id`
3. `POST /beta/copilot/conversations/{id}/chat`
4. send `{ message, locationHint, additionalContext?, contextualResources? }`
5. keep the `conversationId` for the next turn

See:
- [dotnet-copilot-chat-request.cs](sample_codes/dotnet-copilot-chat-request.cs)
- [javascript-copilot-chat-request.mjs](sample_codes/javascript-copilot-chat-request.mjs)

### Pattern 2: Route context by capability, not by convenience

Use the same separation ContextRelayVS adopted:

- **SharePoint / OneDrive HTTPS URLs** that can be treated as files → `contextualResources.files`
- **Pinned snippets, search summaries, and plain text context** → `additionalContext`
- **Large text bodies** → truncate deterministically and mark truncation

This keeps requests auditable and avoids turning every attachment into an opaque prompt blob.

### Pattern 3: Normalize the time zone before serialization

Recommended rule:

1. if the runtime already gives an IANA zone, send it
2. if the value is not IANA and you do not have a trusted mapping layer, send `Etc/UTC`
3. never pass Windows IDs through unchanged to `locationHint.timeZone`

This exact rule prevents the `IANA format` 400 error that ContextRelayVS hit.

### Pattern 4: Work IQ A2A v1.0 request

Minimum protocol requirements:

- `POST https://workiq.svc.cloud.microsoft/a2a/`
- `Authorization: Bearer <token>`
- `A2A-Version: 1.0`
- JSON-RPC envelope with `jsonrpc`, `id`, `method`, `params`
- `method: "SendMessage"`
- message metadata containing `Location.timeZone` and `Location.timeZoneOffset`

See:
- [dotnet-workiq-send-message.cs](sample_codes/dotnet-workiq-send-message.cs)
- [javascript-workiq-send-message.mjs](sample_codes/javascript-workiq-send-message.mjs)

## Investigation Checklist

1. **Surface selection**
   - Are you targeting Graph Copilot Chat or Work IQ?
   - Does the selected surface actually support the scenario you want?
2. **Tenant readiness**
   - Is the user licensed for Microsoft 365 Copilot?
   - Are the app registration and delegated permissions complete?
   - Was admin consent granted?
3. **Conversation state**
   - Are you persisting `conversationId` or `contextId` correctly?
   - Do you clear it when the UX says the conversation was reset?
4. **Payload shape**
   - Are text and file resources separated correctly?
   - Are you truncating large text context predictably?
5. **Location metadata**
   - Is the time zone IANA or `Etc/UTC`?
   - Is the offset expressed with the sign the API expects?
6. **Protocol evidence**
   - Capture endpoint, request body, status code, error code, and request ID before refactoring

## Known Failure Signatures

| Symptom | Likely cause | Fix |
|---|---|---|
| `400 Bad Request` with an `IANA format` complaint | `locationHint.timeZone` used a Windows ID such as `Tokyo Standard Time` | Send an IANA value or `Etc/UTC` |
| `403 Forbidden` with Copilot license wording | User is not licensed for Microsoft 365 Copilot | Assign the license, wait for propagation, then retry |
| `403 Forbidden` from Work IQ | Missing `WorkIQAgent.Ask` permission, missing admin consent, or missing license | Recheck the app registration and tenant setup |
| JSON-RPC `-32601 Method not found` from Work IQ | Missing `A2A-Version: 1.0` header | Send `A2A-Version: 1.0` explicitly |
| Empty or weak headless Work IQ response | Recent license assignment delay or an Office-hosted agent that does not work headlessly | Retry later or use a supported agent scenario |
| Repeated context bloat or odd answers | Large prompt text mixed with file resources indiscriminately | Split context by type and enforce a text budget |

## Guardrails

- Do not assume **Graph beta** payloads are stable; isolate request-shape code.
- Do not pass **Windows time-zone IDs** into Copilot Chat.
- Do not treat **`WorkIQAgent.Ask`** as a Microsoft Graph permission; it is a separate resource permission.
- Do not hide request IDs or status codes when surfacing failures.
- Do not rebuild multi-turn state by replaying the full transcript if the service already gives you a conversation handle.
- Do not mutate files or editor content implicitly just because Copilot returned text; keep edits explicit in the UX.

## Learn More

| Topic | How to Find |
|-------|-------------|
| Microsoft 365 Copilot extensibility overview | `microsoft_docs_fetch(url="https://learn.microsoft.com/microsoft-365/copilot/extensibility/overview")` |
| Work IQ API quickstart | `microsoft_docs_fetch(url="https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq-api-quickstart")` |
| Work IQ API overview | `microsoft_docs_search(query="Work IQ API overview Microsoft 365 Copilot")` |
| Work IQ CLI / samples | `microsoft_docs_search(query="Work IQ CLI Microsoft 365 Copilot")` |
| Microsoft Graph JavaScript SDK install | `microsoft_docs_search(query="Microsoft Graph JavaScript SDK installation")` |
| Microsoft Graph .NET SDK install | `microsoft_docs_search(query="Microsoft Graph .NET SDK installation")` |

## CLI Alternative

If the Learn MCP server is not available, use the `mslearn` CLI instead:

| MCP Tool | CLI Command |
|----------|-------------|
| `microsoft_docs_search(query: "...")` | `mslearn search "..."` |
| `microsoft_code_sample_search(query: "...", language: "...")` | `mslearn code-search "..." --language ...` |
| `microsoft_docs_fetch(url: "...")` | `mslearn fetch "..."` |

Run directly with `npx @microsoft/learn-cli <command>` or install globally with `npm install -g @microsoft/learn-cli`.

**Bash**

```bash
npx @microsoft/learn-cli search "Microsoft 365 Copilot extensibility overview"
npx @microsoft/learn-cli fetch "https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq-api-quickstart"
```

**PowerShell 7**

```powershell
npx @microsoft/learn-cli search "Microsoft 365 Copilot extensibility overview"
npx @microsoft/learn-cli fetch "https://learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq-api-quickstart"
```
