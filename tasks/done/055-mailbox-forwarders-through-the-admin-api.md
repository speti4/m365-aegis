---
size: S
origin: done/048-mailbox-list-through-the-admin-api.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M2 Exchange
---

# 055 — `mailbox forwarders` through the Admin API

## Context

`mailbox forwarders` reads the Admin API's mailbox list with the three forwarding fields in `$select` and the
accepted domains from its `AcceptedDomain` endpoint, and starts the sidecar only for the recipient targets it has
to resolve.
Why: since task 048 the list runs without a sidecar, and the forwarders read is the same list with three more
fields; the 2.4 s cold connect is paid for the recipient targets alone.
Done when: `mailbox forwarders` answers from fixtures and live with backend `admin-api` when no forwarder targets
a recipient, `admin-api+pwsh` otherwise, and the layer table names it.
Part of the Exchange and window bundle (055, 059, 056, 057) of the round of 2026-10-30; the task session owns the
steps below and may reshape them. The live line (the backend, the pwsh child count, the timing) comes from the main
session's signed-in round, `--dry-run` and reads only. The parallel writes bundle (058, 061, 060, 062) adds
`Add-/Remove-DistributionGroupMember` to the sidecar's allow-list and the fake module; this task changes a read only.

## Docs that move

- `docs/development.md`, "The Exchange Online sidecar": the layer table row of `mailbox forwarders`
- `README.md`: the bundle's own heading, if the first run's text names the forwarders read

## Not in this change

- the sidecar's own forwarders read (stays as the fallback when the Admin API cannot answer)
- the window's forwarders page (none exists)

## Steps

1. [x] ~~`mailbox forwarders` over the Admin API's `Get-Mailbox` with the forwarding fields in `$select`~~: built,
   measured live, and taken back out (see Deviations); the read stays on the sidecar
2. [x] ~~The sidecar started only for the recipient targets to resolve~~: went with step 1
3. [x] The live probe of the ways to ask the Admin API for the forwarding fields (`probe admin-api --forwarding`,
   `--mailbox-parameters`, `--select`) and its Core tests
4. [x] Docs: the layer table keeps `mailbox forwarders` on the sidecar with why, the probe's options
5. [x] Live probe (main session): no way brings the fields back

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window bundle (055, 059, 056, 057)
- 2026-10-30 — first build: `MailboxLookups.ForwardersAsync(ExchangeBackends, …)` read the Admin API's `Get-Mailbox`
  over every page with `$select` on the list's fields plus `ForwardingSmtpAddress`, `ForwardingAddress`,
  `DeliverToMailboxAndForward`, and `Get-AcceptedDomain` side by side, matched a `ForwardingAddress` against the list's
  mailboxes and started the sidecar only for a target no mailbox answered to; made-up fixtures of the list with the
  forwarding fields and 8 Core and Cli tests held it offline.
- 2026-10-30 — the main session's first live run of it: exit 0, backend `admin-api`, 621 mailboxes, 13 accepted
  domains, 0 forwarders, 11344 ms, no pwsh child, where the sidecar's read of the same tenant finds 38. The Admin
  API's `Get-Mailbox` answers the endpoint's core properties only (15–16 names: Alias, DisplayName, EmailAddresses,
  ExternalDirectoryObjectId, GrantSendOnBehalfTo(WithDisplayNames), HiddenFromAddressListsEnabled, Id, Identity,
  MaxSendSize, Name, PrimarySmtpAddress, RecipientType, RecipientTypeDetails, UserPrincipalName), as its
  documentation says (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-mailbox: the parameters are
  `Identity`, `ResultSize` and `IncludeGrantSendOnBehalfToWithDisplayNames`, the response is limited to the core
  properties). Task 048's list never needed more than these, so the limit had not shown.
- 2026-10-30 — `probe admin-api --forwarding` (`Exchange/ExchangeProbes.cs`, kept): `Get-Mailbox` of `--mailbox` (else
  one mailbox) in four ways, the `$select` of the three fields and the `Get-EXOMailbox` body parameters `Properties`,
  `PropertySets: Delivery` and `PropertySets: All`, each printed as its status and the forwarding fields that came
  back; `--mailbox-parameters '<JSON object>'` with `--select` tries one more body. Live (the main session): `$select`
  200 with the answer's shape `Identity` alone (honored, the three names dropped), `Properties` 400 "Invalid
  parameter(s) for cmdlet 'Get-Mailbox': Properties", both `PropertySets` 400 with the same for `PropertySets`, and
  `{"PropertySets":["Minimum","Delivery"]}` 400 too; 81–153 ms a call.
- 2026-10-30 — the maintainer's decision: `mailbox forwarders` stays on the sidecar (no Admin API list paid for
  nothing), the layered read, its fallback, its fixtures and its tests are taken out, and the probe's options stay as
  the maintainer's tool with their 3 Core tests.

- 2026-10-30 — closed by the main session on `main` after #25; the follow-up "forwarders through the Admin API if a later version of the `Mailbox` endpoint carries the forwarding fields" is not filed: nothing to do until Microsoft extends the endpoint, and this file and `docs/development.md` keep `probe admin-api --forwarding` as the re-check

## Deviations from the plan

- the task's done-when cannot be met on the documented Admin API: its `Mailbox` endpoint does not carry the forwarding
  fields and refuses the parameters that would ask for them, so `mailbox forwarders` keeps reading through the
  sidecar (backend `pwsh`) as before; what this task leaves is the live probe and the layer table's reason. A later
  version of the endpoint can be checked with `probe admin-api --forwarding` (a follow-up)

## Verification

- 2026-10-30, live (the main session's round, the broker's cached token, reads only): this branch's first build,
  `mailbox forwarders`: backend `admin-api`, 621 mailboxes, 0 forwarders, 11344 ms, no pwsh child; the sidecar's read
  (bundle B's build, `pwsh`): 620 mailboxes, 13 accepted domains, 38 forwarders (15 external, kinds `recipient` and
  `smtp-address`), 11493 ms. `probe admin-api --mailbox <a forwarding mailbox> --forwarding`: `Mailbox (select)` 200,
  forwarding fields none (shape `value[0].Identity: string`); `Mailbox (properties)` 400 `BadRequest Invalid
  parameter(s) for cmdlet 'Get-Mailbox': Properties`; `Mailbox (property-sets-delivery)` and `(property-sets-all)` 400,
  the same for `PropertySets`; `--mailbox-parameters '{"PropertySets":["Minimum","Delivery"]}'` 400; token permissions
  Exchange.Manage, Exchange.ManageV2, User.Read, User.ReadBasic.All.
- 2026-10-30, local (the worktree, after the read went back to the sidecar): see the bundle's matrix line in 059's
  Verification; the sidecar's forwarders tests and the command line's snapshots unchanged, the probe's 3 tests pass.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
