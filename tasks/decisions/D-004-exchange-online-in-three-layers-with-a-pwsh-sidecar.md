---
status: decided
date: 2026-10-28
cost: medium
sources: [wip/001-project-scaffolding.md, docs/research/2026-10-28-m365-admin-tooling-landscape.md, D-002-dotnet-10-core-cli-and-window-with-react.md]
---

# D-004 — Exchange Online in three layers behind one interface: Graph, the Admin API, a pwsh sidecar with an allow-list

## Context

Most of the mailbox domain (FullAccess and SendAs, forwarding, shared mailboxes, retention, transport rules,
live sizes) exists only in Exchange Online PowerShell. Graph has message trace, the usage report and mailbox
settings; the Exchange Online Admin API (preview since 2025-11) has six endpoints and is "not a replacement" for
the module; the `adminapi/beta/InvokeCommand` endpoint the module uses is undocumented and unsupported.

## Decision

Exchange Online is reached through one interface in the core (`IExoBackend`) with three backends, picked per
operation:

1. **Graph**, where the feature exists: message trace (v1.0), the mailbox usage report, mailbox settings and
   inbox rules, mailbox import and export.
2. **The Exchange Online Admin API v2.0**, for its documented endpoints (Send on Behalf, folder permissions,
   distribution group membership, organization and accepted domains), once task 010 shows the tenant has it and
   a public client can hold `Exchange.ManageV2`.
3. **A pwsh 7.6 sidecar** (`src/M365Aegis.Core/Exchange/Sidecar/aegis-exo-sidecar.ps1`) running the
   `ExchangeOnlineManagement` module for everything else: a long-lived child process driven over stdin and
   stdout with one JSON object per line, tagged with the protocol version; the protocol owns stdout and
   everything else the process prints goes to stderr; **only the cmdlets on the script's allow-list run**;
   `Connect-ExchangeOnline -AccessToken` with the token the core acquired (task 009 verifies it; the module's own
   sign-in is the fallback). The window keeps the sidecar warm; the command line starts it on demand.

The module and pwsh 7.6 are prerequisites the `doctor` checks, not something the tool ships. The undocumented
`InvokeCommand` endpoint is never called. In-process hosting through `Microsoft.PowerShell.SDK` stays an option
for later if the sidecar proves too narrow. Cost: medium. Decided 2026-10-28.

## Note 2026-10-29 — the sidecar connects with the broker's token (task 009)

The sidecar connects with `Connect-ExchangeOnline -AccessToken <the broker's token for
https://outlook.office365.com/.default> -Organization <tenant>.onmicrosoft.com -SkipLoadingFormatData
-CommandName <the REST cmdlets of the allow-list>`; one token carries `Exchange.Manage` and `Exchange.ManageV2`;
the core renews it with a `reconnect` five minutes before its expiry (375 ms); the module's own sign-in is not
needed, so the one sign-in of D-003 serves Graph and Exchange Online. Cold on the maintainer's machine: pwsh to
hello 0.45 s, the module's import 1.2 s, the connect 0.48 s (task 011; `docs/origin.md` has the three variants).

## Note 2026-10-29 — the Admin API answers in the tenant (task 010)

The Admin API v2.0 answers in the tenant with the broker's delegated token (`Exchange.ManageV2` on the same
`outlook.office365.com` resource as the sidecar; POST with a `CmdletInput` body, `X-AnchorMailbox: UPN:<admin>`;
`OrganizationConfig` 200 in 0.6 s, `Mailbox` 200 in 0.16 s with `@odata.nextLink` paging). The second layer
stands: an Admin API backend (backlog 042) serves Send on Behalf, folder permissions, distribution group
membership and the organization reads.

## Note 2026-10-30 — the second layer exists (task 042)

An Admin API backend (`Exchange/AdminApi/AdminApiClient`) implements the same `IExoCmdletRunner` interface as the
sidecar and answers the mailbox with its Send on Behalf delegates by display name, the folder permissions, the
distribution group members and the organization reads, each in about a second after one sign-in; its own
allow-list is held to the same rule as the sidecar's (exit 12 for a cmdlet off it). `mailbox access` reads the
Admin API and the sidecar side by side with the sidecar as the fallback, and `meta.backend` names the layers used
(`admin-api+pwsh`). The layer table in `docs/development.md` says which read goes through which layer. The
reverse lookup splits its FullAccess read across four sidecars by default (task 043: 122 s to 41 s over 620
mailboxes, no gain past four), and the sizes over every mailbox come from Graph's usage report (16 s, a few days
old, no sidecar); the live cmdlets stay behind `--method`.

## Note 2026-10-30 — the write cmdlets join the allow-list (task 053)

The mailbox writes of M3 run through the sidecar, the one layer with documented writes: the allow-list admits
`Add-MailboxPermission`, `Remove-MailboxPermission` (FullAccess), `Add-RecipientPermission`,
`Remove-RecipientPermission` (Send As) and `Set-Mailbox` (Send on Behalf through `-GrantSendOnBehalfTo @{Add=…}` /
`@{Remove=…}`, and the forwarding). A write cmdlet also has a parameter allow-list (`ExoCmdlets.WriteParameters` and
the script's `$script:WriteParameters`, held equal by a test): `Set-Mailbox` can change anything on a mailbox, so it
runs with `Identity`, `GrantSendOnBehalfTo`, `ForwardingSmtpAddress`, `ForwardingAddress`,
`DeliverToMailboxAndForward` and `Confirm` only, and any other parameter is refused before anything is sent (exit
code 12), by the core and again by the script. Every write passes `-Confirm:$false` and runs only from a plan's apply
(D-006, task 051). The Admin API stays read-only: its documented endpoints are reads, and `InvokeCommand` is still
never called.

## Not chosen

- Graph only: it cannot cover the mailbox domain the tool exists for.
- The undocumented `adminapi/beta/InvokeCommand`: it would run every cmdlet over REST with no process to host,
  but Microsoft does not support it and it can change at any time.
- In-process hosting from the start: it pins the build to `Microsoft.PowerShell.SDK` 7.6 and net10.0, rules out
  trimming and NativeAOT, and risks MSAL assembly clashes between the host and the module; the sidecar is
  debuggable with a text editor and swappable.
- A sidecar that runs any cmdlet the caller names: the allow-list is the safety net the hand-off to the team
  needs.
