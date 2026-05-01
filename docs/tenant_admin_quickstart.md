# ContextRelay for Visual Studio Tenant Admin Quickstart

Use this guide to create or validate the Microsoft Entra app registration required by ContextRelay for Visual Studio.

ContextRelay signs in as a **public client** with **delegated Microsoft Graph permissions**. It supports **work or school accounts only**. Personal Microsoft accounts are not supported.

## 1. What you need

- A Microsoft Entra tenant where you can create or manage app registrations
- A work or school account that can grant consent, or a tenant admin who can grant admin consent
- Visual Studio 2022 / 2026 with the ContextRelay VSIX installed

## 2. Register the application

1. Open **Microsoft Entra admin center** > **Applications** > **App registrations**.
2. Select **New registration**.
3. Enter an application name such as `ContextRelayVS`.
4. Choose the supported account type:
   - **Single tenant** if the extension is only for your organization
   - **Multitenant (organizations only)** if users will sign in from multiple Entra tenants
5. Complete the registration and copy the **Application (client) ID**.

## 3. Enable public-client sign-in

1. Open the app registration.
2. Go to **Authentication**.
3. Enable the setting for **public client / mobile and desktop flows**.
4. Save the change.

ContextRelay uses MSAL.NET with the default public-client redirect flow (`WithDefaultRedirectUri()`), so no custom confidential-client secret is required.

## 4. Add Microsoft Graph delegated permissions

Open **API permissions** > **Add a permission** > **Microsoft Graph** > **Delegated permissions**, then add the permissions that match the features you want to enable.

| Feature | Required delegated permissions |
|---|---|
| Base sign-in | `User.Read` |
| Mail search | `Mail.Read` |
| Teams search | `Chat.Read`, `ChannelMessage.Read.All` |
| SharePoint / OneDrive retrieval | `Files.Read.All`, `Sites.Read.All` |
| Connectors | `ExternalItem.Read.All` |
| Plain Copilot chat and `/ask` context chat preview | `Mail.Read`, `Sites.Read.All`, `People.Read.All`, `OnlineMeetingTranscript.Read.All`, `Chat.Read`, `ChannelMessage.Read.All`, `ExternalItem.Read.All` |
| `/workiq` Work IQ queries | `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask` |

Notes:

- Some permissions overlap across features; duplicates do not matter.
- If you leave **Enable chat preview** off, you do not need the extra Copilot chat preview permissions.
- `offline_access`, `openid`, and `profile` are requested by MSAL during sign-in and do not need to be added as Microsoft Graph API permissions.

## 4b. Add Work IQ delegated permission (optional, required for `/workiq`)

`WorkIQAgent.Ask` is **not** a Microsoft Graph permission. It belongs to the separate Work IQ resource endpoint used by `/workiq`.

1. Provision the Work IQ service principal in your tenant if it does not already exist:

   ```http
   POST https://graph.microsoft.com/v1.0/servicePrincipals
   Content-Type: application/json

   {
     "appId": "fdcc1f02-fc51-4226-8753-f668596af7f7"
   }
   ```

2. In **Microsoft Entra admin center** > **App registrations** > your ContextRelay app > **API permissions** > **Add a permission** > **APIs my organization uses**, search for **Work IQ**.
3. Add the delegated permission **WorkIQAgent.Ask**.
4. Grant tenant admin consent.

Users also need a **Microsoft 365 Copilot license** before `/workiq` queries can succeed.

## 5. Grant tenant consent

After adding permissions:

1. Stay on **API permissions**.
2. Select **Grant admin consent** if your tenant requires admin approval for one or more delegated permissions.
3. Confirm consent status is granted for the permissions you plan to use.

If admin consent is not granted where required, sign-in can fail with errors such as `AADSTS65001` or `AADSTS65002`.

## 6. Configure ContextRelay in Visual Studio

Open **Tools** > **Options** > **ContextRelay** > **Authentication** and set:

- **Client ID**: the Entra application (client) ID from the app registration
- **Tenant ID**: a tenant GUID or an authority segment such as `organizations`

Recommended values:

| Scenario | Tenant ID |
|---|---|
| One organization only | Your tenant GUID |
| Multiple work/school tenants | `organizations` |

The token cache is stored at:

```text
%LocalAppData%\ContextRelayVS\msal.cache
```

## 7. Validate the setup

1. Open the ContextRelay tool window.
2. Sign in when prompted.
3. Run a simple command such as `/mail test` or `/all test`.
4. If you enabled chat preview, also test a plain chat message and `/ask summarize`.
5. If you granted `WorkIQAgent.Ask`, also test `/workiq What meetings do I have today?`.
5. Follow the broader host validation steps in [e2e_checklist.md](e2e_checklist.md).

## 8. Troubleshooting

### Missing client ID

If ContextRelay reports that `contextRelay.auth.clientId` is missing, fill in the **Authentication** options page before retrying sign-in.

### Consent or permission failures

If you see `AADSTS65001`, `AADSTS65002`, a Graph permission error, or `/workiq` returns `403 Forbidden`:

- verify the required delegated permissions were added
- verify admin consent was granted where your tenant requires it
- verify the enabled ContextRelay features match the permissions granted
- verify Work IQ users have a Microsoft 365 Copilot license

### Broker or interactive sign-in failures

ContextRelay uses **MSAL.NET** with the **Windows Account Manager (WAM)** broker. If interactive sign-in fails:

- confirm the machine can show the Windows account picker
- confirm the signed-in account is a work or school account
- confirm the tenant and supported account type match your registration

### Personal Microsoft accounts

Personal Microsoft accounts are not supported. Use an Entra ID work or school account.
