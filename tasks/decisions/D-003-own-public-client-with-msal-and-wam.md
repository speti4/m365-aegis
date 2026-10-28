---
status: decided
date: 2026-10-28
cost: medium
sources: [wip/001-project-scaffolding.md, D-002-dotnet-10-core-cli-and-window-with-react.md]
---

# D-003 — Our own public-client app registration, MSAL.NET with the Windows account manager, delegated scopes

## Context

The tool signs in as the administrator and later as members of the support team, on Entra-joined Windows
machines under Conditional Access. The maintainer is a Global Administrator of the tenant and received the
manager's approval for an app registration of the tool's own on 2026-10-28. An earlier draft borrowed the
Microsoft Graph PowerShell SDK's first-party client id.

## Decision

- A **public-client app registration of our own** in the tenant (single tenant), with the "Mobile and desktop"
  platform and the broker redirect URI; its ids stay out of the repo (`CLAUDE.local.md`, the home folder's
  `config.json`).
- **MSAL.NET with the broker** (`Microsoft.Identity.Client` and `.Broker`): the Windows account manager first, so
  the signed-in Entra account signs in silently and Conditional Access (device-based, token protection) is
  satisfied; the interactive account picker when silent fails; device code where there is no window (an SSH
  session, a non-interactive run). The MSAL cache is persisted through `Microsoft.Identity.Client.Extensions.Msal`
  in a DPAPI-protected file under the home folder, shared by the command line and the window.
- **Delegated scopes with admin consent**, the least that the three domains need, documented in
  `docs/development.md` as task 002 settles them; per-resource tokens for `graph.microsoft.com` and
  `outlook.office365.com` from one sign-in.
- **App-only later**, as a separate certificate profile, only for what needs it: the Teams chat export
  (`getAllMessages` is an application-permission protected API).
- Cost: medium. The registration and the scopes can change; the broker and MSAL shape the auth code. Decided
  2026-10-28.

## Not chosen

- The Graph PowerShell SDK's first-party client id (the earlier draft): not ours to depend on, and useless for the
  Exchange Online scopes.
- A client secret anywhere: a public client carries no credential; app-only uses a certificate.
- Hand-rolled OAuth (device code and PKCE over `oauth2`-style libraries): what the Rust stack would have needed;
  no broker, no Conditional Access story, a cache of our own to secure.
