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

## Not chosen

- Graph only: it cannot cover the mailbox domain the tool exists for.
- The undocumented `adminapi/beta/InvokeCommand`: it would run every cmdlet over REST with no process to host,
  but Microsoft does not support it and it can change at any time.
- In-process hosting from the start: it pins the build to `Microsoft.PowerShell.SDK` 7.6 and net10.0, rules out
  trimming and NativeAOT, and risks MSAL assembly clashes between the host and the module; the sidecar is
  debuggable with a text editor and swappable.
- A sidecar that runs any cmdlet the caller names: the allow-list is the safety net the hand-off to the team
  needs.
