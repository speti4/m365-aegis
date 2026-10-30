---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 053 — Mailbox permission and forwarding writes through the sidecar

## Context

`mailbox access grant|revoke <mailbox> --user <upn> --right FullAccess|SendAs|SendOnBehalf` and
`mailbox forwarding set|clear <mailbox>` as plans of task 051, run by the sidecar (`Add-/Remove-MailboxPermission`,
`Add-/Remove-RecipientPermission`, `Set-Mailbox` with `-GrantSendOnBehalfTo`, `-ForwardingSmtpAddress`,
`-DeliverToMailboxAndForward`); the allow-list grows in `ExoCmdlets.cs` and the script with a D-004 note on the
writes, the fake module gets the cmdlets and Pester covers them, the Admin API stays read-only (its documented
endpoints are reads); the plan reads the current grants first (the lookups of 016 and 044), so it is current → desired.
Why: the roadmap's M3 "done when" is a mailbox permission grant that runs as a dry run, then an apply.
Done when: `mailbox access grant --dry-run` against the fake module skips a grant already held, `--yes` applies
through the sidecar and the fake records the call, the forwarding pair does the same, and the live `--dry-run`
against the tenant lists the real holders without applying anything.
Part of the M3 writes bundle (051, 052, 053, 054) of the round of 2026-10-30; builds on 051. The parallel bundle
(046–050) asserts in task 050 that the fake module's exports equal the allow-list, so every cmdlet added here is
also exported by the fake module's manifest. Nothing is applied in the tenant this round (decided 2026-10-30).

## Docs that move

- `docs/development.md`: the writes in "The Exchange Online sidecar" and the layer table, and in "Writes and plans"
- `tasks/decisions/D-004-…`: a dated note on the write cmdlets of the allow-list
- `README.md`: the commands under the bundle's own heading
- `tests/sidecar/README.md`: the fake module's write cmdlets and what they record

## Not in this change

- writes through the Admin API (no documented write endpoint)
- folder permission writes, mailbox creation, conversion to shared, litigation hold
- the CSV batch over these writes (054)

## Steps

1. [x] The allow-list: the five write cmdlets in `ExoCmdlets.cs` and `$script:AllowedCmdlets`, a parameter
   allow-list per write cmdlet (`ExoCmdlets.WriteParameters`, `$script:WriteParameters`), the D-004 note
2. [x] The fake module's write cmdlets (the data changes in the process, each call recorded in the file
   `M365AEGIS_FAKE_EXO_WRITES` names, the failing identities and the mail contact), the Pester cases
3. [x] The plans: the current grants (`MailboxLookups.AccessAsync` over the layers) and forwarding, each holder as a
   recipient, the diff, the warnings (an external address, a recipient target that takes precedence, no copy kept,
   a grant to a group, a holder that is no security principal)
4. [x] The apply through the sidecar, per-change results, the plan record
5. [x] The four commands with `--dry-run` and `--yes`; Core and Cli tests
6. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the M3 writes bundle (051, 052, 053, 054)
- 2026-10-30 — allow-list: `Add-MailboxPermission`, `Remove-MailboxPermission`, `Add-RecipientPermission`,
  `Remove-RecipientPermission`, `Set-Mailbox`, in `ExoCmdlets.Allowed` and the script; `Set-Mailbox` runs with
  `Identity`, `GrantSendOnBehalfTo`, `ForwardingSmtpAddress`, `ForwardingAddress`, `DeliverToMailboxAndForward` and
  `Confirm` only, refused otherwise by the session (exit 12, before the sidecar starts) and by the script; the script
  turns a JSON object parameter into a hashtable (`@{Add=…}`); the connect's `-CommandName` now imports the write
  cmdlets too. The tests that used `Set-Mailbox` as the refused example use `Remove-Mailbox`. D-004 note of
  2026-10-30.
- 2026-10-30 — `Exchange/MailboxWrites.cs` (a part of `MailboxLookups`, so its private reads are reused without
  touching `MailboxLookups.cs`): `PlanAccessAsync` reads each holder with `Get-EXORecipient` (a missing one exit 4)
  and each mailbox as `mailbox access` does, matches a holder by every name of the recipient, and plans
  `Add-/Remove-MailboxPermission -AccessRights FullAccess -InheritanceType All`, `Add-/Remove-RecipientPermission
  -AccessRights SendAs` or `Set-Mailbox -GrantSendOnBehalfTo @{Add|Remove}`, each with `-Confirm:$false`;
  `PlanForwardingAsync` reads `Get-EXOMailbox -Properties ForwardingSmtpAddress,ForwardingAddress,DeliverToMailboxAndForward`
  and `Get-AcceptedDomain` and plans `Set-Mailbox -ForwardingSmtpAddress … -DeliverToMailboxAndForward …` (clearing
  a `ForwardingAddress`, which takes precedence: Microsoft's "Respond to a compromised email account", read
  2026-10-30) or the clear with both `$null`.
- 2026-10-30 — `mailbox access grant|revoke <mailbox> --user … --right …` (`--user` and `--right` repeat, a right
  also comma-separated) and `mailbox forwarding set <mailbox> --to <address> [--keep-copy]`, `clear <mailbox>`, with
  `--from-csv`, `--dry-run` and `--yes`.
- 2026-10-30 — tests: Pester 34 passed (7 new: a grant read back and recorded, Send on Behalf added and removed, the
  forwarding set and cleared, an off-list parameter refused with nothing recorded, the mail contact refused, a revoke
  of a right nobody holds and a throttled write; the connect's `CommandName` case updated); PSScriptAnalyzer 0
  findings over the three folders; `MailboxWritesTests` (14: grant skips a held right and applies through the
  sidecar, Send on Behalf both ways, Send As revoke, the Admin API's delegates through the layers, the group and
  contact warnings and the partial apply, missing holder and mailbox, the forwarding comparisons and warnings, the
  recipient target replaced and the clear, a mailbox named twice, the CSV batch, the forwarding CSV, the rights'
  spellings, the arguments, the script's parameter lists against the core's, the refusal before the sidecar
  starts); `WriteCommandTests` (the grant with the fake's record of `Add-MailboxPermission`, the CSV of three grants,
  the forwarding set with `Set-Mailbox`'s parameters, the clear that is up to date).
- 2026-10-30 — docs: `docs/development.md` ("The write cmdlets" in the sidecar section, three rows of the layer
  table, the writes in "Writes and plans"), `README.md` "Writes behind a plan", `tests/sidecar/README.md` (the fake's
  write cmdlets and their record), the D-004 note.
- 2026-10-30 — the connect with the twelve cmdlets of the allow-list, measured live by the main session (`probe
  exchange-measure --runs 3 --variants default`): every cmdlet answered (`missing: []`); medians total 3847 ms,
  start 474 ms, import 1161 ms, connect 1726 ms, first call 370 ms. The start matches task 011's 0.48 s; importing
  the five write cmdlets with `-CommandName` did not move it.
- 2026-10-30 — merged `origin/main` after bundle B (7001a79): one conflict, in `tests/sidecar/sidecar.Tests.ps1`
  (`Invoke-Sidecar` takes B's `-ModuleRoot` and this bundle's `-Environment`; both new `Describe` blocks kept); B's
  test that the fake module exports exactly the allow-list passes with the five write cmdlets.

- 2026-10-30 — closed by the main session on `main` after #24; the approved follow-ups of the pull request are filed as backlog items 055–062

## Deviations from the plan

- Beyond the cmdlet allow-list, the write cmdlets got a parameter allow-list (core and script, held equal by a
  test): `Set-Mailbox` would otherwise admit any change to a mailbox.
- The writes are a second file of the partial `MailboxLookups` class, which keeps `MailboxLookups.cs` and
  `MailboxReads.cs` (bundle B's) untouched.
- `--user` and `--right` take several values (each user with each right), which is what the CSV batch plans too.

## Verification

- 2026-10-30 — local, against the fake module: `mailbox access grant finance --user alice@contoso.com --user
  bob@contoso.com --user finance-staff@contoso.com --right FullAccess,SendAs --right SendOnBehalf --dry-run` exit 0
  (6 changes, 3 held already, the group's warning); `--yes` for bob and the mail contact exit 6 (bob applied, the
  contact's two `failed: cmdlet-failed`); `revoke` of alice's Send As and FullAccess exit 0; `mailbox forwarding set
  carol --to carol@fabrikam.com --keep-copy --yes` exit 0 with the precedence and external warnings and the fake's
  record of `Set-Mailbox` with `ForwardingAddress: null`; `forwarding clear alice` exit 0 `up-to-date`.
- 2026-10-30 — live, `--dry-run` only: `mailbox access grant <shared mailbox> --user <holder> --user <non-holder>
  --right FullAccess,SendAs,SendOnBehalf` (a shared mailbox with 6 FullAccess user holders) exit 0, 5 changes and the
  holder's FullAccess unchanged ("holds FullAccess already"): the tenant's permission entry matched the recipient's
  names; backend `pwsh+admin-api`, 2 requests, 4239 ms (4341 ms wall); `revoke <mailbox> --user <holder> --right
  FullAccess` exit 0, 1 change, 4014 ms; `mailbox forwarding set <mailbox> --to nobody@example.com` exit 0, 1 change,
  the two warnings (outside the accepted domains, keeps no copy), backend `pwsh`, 3758 ms; `forwarding clear` exit 0,
  1 change, about 3.6 s, for a mailbox forwarding to a recipient and for one forwarding to an external SMTP address,
  both keeping a copy. `Get-EXORecipient -Identity <upn>` resolved every holder; no existing object came back not
  found. Nothing was applied.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
