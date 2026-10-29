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

## Note 2026-10-29 — the broker from a console process (task 013)

The spike of task 013 confirmed the broker from a console process on the maintainer's machine, which is
workplace-registered to the tenant rather than Entra-joined, over a Remote Desktop session:

- The silent `OperatingSystemAccount` path fails at once on a registered-only machine (no primary refresh
  token); it stays first in the order for Entra-joined machines, and its failure costs half a second.
- An account the broker lists signs in silently with no UI, from any console and with every standard stream
  redirected: the broker, not a persisted cache, is what makes the second run silent. The broker can list
  several accounts of one tenant, so the account is chosen by user principal name from the profile, never as the
  first in the list.
- Interactive sign-in through the broker works in Windows Terminal and conhost with the parent window taken from
  `GetAncestor(GetConsoleWindow(), GA_ROOTOWNER)`. Without a console window it fails immediately with
  `window_handle_required`, which is the signal to fall back to device code.
- The tenant's Conditional Access lets device code through, so the last fallback holds.
- `Exchange.ManageV2` sits in the "Office 365 Exchange Online" API and was granted to the public client with
  admin consent next to `Exchange.Manage`.

The fallback order of task 002 follows: silent with the operating system account, silent with the profile's
account, interactive through the broker when a window exists, device code otherwise.

## Note 2026-10-29 — the broker's silent path never reaches the file cache's refresh token (task 002)

The signed-in round of task 002 measured a case the decision's text does not cover. After a device-code
sign-in (every stream redirected), the next silent call for the same account failed: with the broker enabled,
MSAL.NET answers a silent call for an account the broker lists from the broker alone and never falls back to the
refresh token the file cache holds from the device-code sign-in. The adapter therefore tries the same account
through a second client over the same cache file without the broker when the broker's silent call fails; that
call answered from the cache in 0.3 s. So the file cache holds the full token set of a device-code sign-in, not
only accounts and id tokens, and it is registered for that reason as much as for sharing the cache between the
command line and the window.

## Not chosen

- The Graph PowerShell SDK's first-party client id (the earlier draft): not ours to depend on, and useless for the
  Exchange Online scopes.
- A client secret anywhere: a public client carries no credential; app-only uses a certificate.
- Hand-rolled OAuth (device code and PKCE over `oauth2`-style libraries): what the Rust stack would have needed;
  no broker, no Conditional Access story, a cache of our own to secure.
