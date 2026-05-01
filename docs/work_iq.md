# Work IQ Integration

ContextRelay for Visual Studio supports querying the Work IQ Gateway through the `/workiq` slash command. Work IQ provides AI-powered access to Microsoft 365 work intelligence such as emails, meetings, files, and organizational knowledge.

## Prerequisites

- A user with a **Microsoft 365 Copilot license**
- A ContextRelay Entra app registration configured under **Tools > Options > ContextRelay > Authentication**
- The **Work IQ service principal** provisioned in your organization
- The **`WorkIQAgent.Ask`** delegated permission added to your app registration with admin consent

## Enable Work IQ in your tenant

### 1. Provision the Work IQ service principal

Use Graph Explorer or another admin-capable client to create the Work IQ service principal if it does not already exist:

```http
POST https://graph.microsoft.com/v1.0/servicePrincipals
Content-Type: application/json

{
  "appId": "fdcc1f02-fc51-4226-8753-f668596af7f7"
}
```

A `201 Created` response means the Work IQ resource is now provisioned in your tenant. A conflict response usually means it already exists.

### 2. Add `WorkIQAgent.Ask` to your app registration

1. Open **Microsoft Entra admin center**.
2. Go to **App registrations** and select the ContextRelay application.
3. Open **API permissions** > **Add a permission** > **APIs my organization uses**.
4. Search for **Work IQ**.
5. Add the delegated permission **WorkIQAgent.Ask**.
6. Grant tenant admin consent.

## Use `/workiq`

Examples:

```text
/workiq Summarize my recent emails from Alice
/workiq What meetings do I have today?
/workiq Find documents about the Q3 budget review
```

Consecutive `/workiq` turns keep using the last returned Work IQ `contextId`. Run `/clear` or use **Clear Chat** to reset that conversation state.

## Protocol details

- Endpoint: `https://workiq.svc.cloud.microsoft/a2a/`
- Protocol: JSON-RPC 2.0 `SendMessage`
- Required header: `A2A-Version: 1.0`
- Token audience: `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`

ContextRelay also sends local time-zone metadata with each request so time-sensitive prompts such as "today" and "this week" are grounded correctly.

## Debug logging

Enable **Work IQ debug logging** under **Tools > Options > ContextRelay > General** to write structural metadata to the **ContextRelay Debug** pane.

Prompt and response bodies are intentionally **not** logged.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `401 Unauthorized` | Re-authenticate. The cached Work IQ token may have expired. |
| `403 Forbidden` | Confirm `WorkIQAgent.Ask` admin consent and a Microsoft 365 Copilot license for the signed-in user. |
| Empty or failed response | Retry after a short delay. Work IQ may still be indexing newly licensed users. |
| `AADSTS65001` / `AADSTS65002` | The app registration is missing tenant-admin consent for Work IQ or Graph. |

## References

- [Work IQ API quickstart](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-quickstart)
- [A2A protocol specification](https://a2a-protocol.org/latest/specification/)
