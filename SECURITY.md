# Security Policy

## Supported versions

The project is in early design / scaffolding phase. Security fixes will be applied to the `main` branch only until the first tagged release.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security reports. Instead, use GitHub's private vulnerability reporting:

1. Go to the Security tab of this repository.
2. Click **Report a vulnerability**.
3. Include reproduction steps, affected component, and any proposed mitigation.

We aim to acknowledge reports within 5 business days.

## Data handled by ContextRelay

- Microsoft 365 delegated Graph responses (user-scoped only).
- Pinned snippets and chat history stored under `%LocalAppData%\ContextRelay\shared\` (user-scope, OS-level ACL).
- MSAL access/refresh tokens stored in the MSAL cache encrypted via DPAPI.
- Access tokens are **never** written to the cross-editor shared store.

## Dependencies

CI runs `dotnet list package --vulnerable --include-transitive` on each build. Critical advisories block release.
