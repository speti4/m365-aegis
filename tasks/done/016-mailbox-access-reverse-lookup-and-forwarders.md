---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M2 Exchange
---

# 016 — Mailbox access in both directions, external forwarders, shared mailboxes and sizes

## Context

`mailbox access <mailbox>` (who holds FullAccess, SendAs and Send on Behalf on it) and `mailbox access
--user <upn>` (which mailboxes that user can open or send as, the reverse lookup over every mailbox through the
`Get-EXO*` bulk cmdlets), `mailbox forwarders` (every mailbox with `ForwardingSmtpAddress` or
`DeliverToMailboxAndForward` set, external ones flagged), `mailbox list --shared` and `mailbox size`
(`Get-EXOMailboxStatistics`); each through the sidecar and the output contract.
Why: the reverse lookup and the forwarder list are the mailbox questions the admin centers do not answer and the
paid tools charge for (the research's "real gap").
Done when: each command answers from the fake module in the tests and live after one sign-in, and the reverse
lookup over the tenant finishes within the sidecar's throttling budget.
Part of the Exchange bundle (009, 015, 016, 011, 010) of the round of 2026-10-29, on the sidecar of task 015;
the task session owns the steps below and may reshape them. The live check runs in the main session's
signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the mailbox commands
- `docs/development.md`, the Exchange Online sidecar: the commands, the cmdlets, the throttling budget
- `tests/sidecar/` README or the fixtures README: the fake module's data

## Not in this change

- any write (permissions, forwarding) — M3
- the Admin API (task 010)

## Steps

1. [x] The fake module grows the cmdlets and the data these need (permissions, forwarding, statistics)
2. [x] `mailbox access <mailbox>`, `mailbox access --user <upn>` (the bulk reverse lookup), `mailbox forwarders`,
       `mailbox list --shared`, `mailbox size`: the lookups, the records, the table views, the snapshots
3. [x] The allow-list grows by exactly the cmdlets used
4. [x] Live check (main session): each command, the reverse lookup timed
5. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange bundle (009, 015, 016, 011, 010)
- 2026-10-29 — built: `Exchange/MailboxLookups.cs` and `Commands/MailboxCommand.cs`. `mailbox access <mailbox>`:
  `Get-EXOMailboxPermission`, `Get-EXORecipientPermission -AccessRights SendAs`, the mailbox's
  `GrantSendOnBehalfTo` resolved with one piped `Get-EXORecipient`; direct, allowed grants only, no `NT
  AUTHORITY` entries, a group not expanded. `mailbox access --user <upn>`: four bulk calls whatever the tenant's
  size (the user as a recipient for its names, every mailbox with `GrantSendOnBehalfTo`, every mailbox's object
  id piped into `Get-EXOMailboxPermission`, the organization's Send As entries), partial (6) when a mailbox's list
  fails. `mailbox forwarders`: `ForwardingSmtpAddress` and `ForwardingAddress` (resolved), external against
  `Get-AcceptedDomain`, external first. `mailbox size [<mailbox>] [--top N]`: `Get-EXOMailboxStatistics`, piped
  for every mailbox, the bytes read from the service's text. The fake module has six made-up mailboxes with
  permissions, forwarding and sizes (`tests/sidecar/README.md`); envelope and table snapshots accepted
- 2026-10-29 — the cost of the bulk calls, read from the live round (some 600 mailboxes): every command pays about
  2.8 s before its first cmdlet (pwsh 0.5 s, the import 1.1 s, the connect 1.3 s); listing every mailbox costs
  about 2.5 s (`mailbox list` 5.4 s in all). `mailbox access --user` took 138 s: the recipient, the mailbox list
  and the organization's Send As list are a few seconds together, so about 125 s, some 0.2 s a mailbox, is
  `Get-EXOMailboxPermission` over every mailbox piped in (some 600): the module sends one REST request per piped identity
  and does not batch them, so that call is the floor of this shape. `mailbox size --top 5` took 80 s, about 0.12
  s a mailbox for `Get-EXOMailboxStatistics`. A `-ResultSize` or a property trim would not cut it: the cost is
  the round trip per mailbox, not the payload. What could: `Get-EXOMailboxPermission` without an identity (the
  cmdlet's documented default parameter set, `-ResultSize Unlimited`), if it lists every mailbox's entries in
  paged calls; the piped mailboxes split across a few sidecars at once; for the sizes of every mailbox, Graph's
  mailbox usage report (a day old, one call). None was throttled in this round. Proposed as a follow-up

## Deviations from the plan

- The allow-list is exactly the cmdlets used: the scaffold's `Get-Mailbox`, `Get-MailboxPermission` and
  `Get-RecipientPermission` left it, `Get-EXORecipient` (names to addresses, the user of the reverse lookup) and
  `Get-AcceptedDomain` (the external flag) joined.
- `mailbox size` takes an optional mailbox and `--top N`.
- The throttling budget is the module's own: the `Get-EXO*` cmdlets retry a throttled request themselves; the
  core sends one call per list, and the module turns the piped mailboxes into one request each (measured, Log).

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the
  Release build with the front end (0 warnings), `dotnet test` 388 green (the Core and Cli tests start the sidecar
  as a real pwsh child against the fake module), `Invoke-Pester -Path tests/sidecar` 20 green, PSScriptAnalyzer
  over the three folders CI checks 0 findings, `changelog.py --check`, `check-fixtures.py` (136 files, 0
  findings); with an empty `M365AEGIS_HOME` `doctor --offline` exited 0 (`sidecar-script` passes on the changed
  script), `whoami` 3, `mailbox list` 3; the window's `--smoke` 0; `release.ps1 -DryRun` green. The new test data
  went through the anonymizer scan, which flagged only the made-up contoso, fabrikam and example addresses
- 2026-10-29 — against the tenant (some 600 mailboxes), in the main session's signed-in round (evening, UTC+1) from
  this branch's Release build, every command exit 0 unless named: `mailbox access <a shared mailbox>` 5494 ms,
  10 holders (FullAccess 6, Send As 4); `mailbox access --user <the admin>` 138427 ms, every mailbox scanned, 0
  grants (the admin account holds no mailbox rights), 0 failed; `mailbox forwarders` 9653 ms, a few dozen forwarders, a
  third of them external; `mailbox size <the shared mailbox>` 4214 ms; `mailbox size --top 5` 80448 ms, 5 rows, the table's
  footer naming 5 of the tenant's mailboxes and their size in all; `mailbox access <no such address>` exit 4 `not-found` with the
  right hint, its message then the service's raw REST error, cut to one sentence afterwards (task 015's Log). The
  reverse lookup finished without throttling; its cost is in the Log

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
