---
size: M
origin: done/016-mailbox-access-reverse-lookup-and-forwarders.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-30
milestone: M2 Exchange
---

# 043 — A faster `mailbox access --user` and `mailbox size` over every mailbox

## Context

The live round of 2026-10-29 measured `mailbox access --user <upn>` at 138 s and `mailbox size --top 5` at
80 s over 620 mailboxes, about 0.2 s and 0.12 s per mailbox, one request each through the piped `Get-EXO*`
cmdlets. Try, in this order: `Get-EXOMailboxPermission` without an identity (`-ResultSize Unlimited`, if it lists
every mailbox's entries in paged calls), the piped mailboxes split across several sidecars at once, and Graph's
mailbox usage report (one call, about a day old) for the sizes; measure each live.
Why: the reverse lookup is the mailbox question the tool exists for, and two minutes is not a question a support
colleague asks twice.
Done when: both commands answer the same tenant in well under a minute, or the Log states the floor of each
cmdlet and the hint names it.
Part of the Exchange M2 bundle (041–044) of the round of 2026-10-29; the task session owns the steps below and
may reshape them. Every live measurement runs in the main session's signed-in round, with the broker's cached
token, never with the module's own sign-in per process.

## Docs that move

- `docs/development.md`, "The lookups": how the reverse lookup and the sizes are read now and what each costs
- `README.md`: the changed timings or the new switch under the bundle's own heading

## Not in this change

- the group expansion of a grant (task 044)
- a cached snapshot of the permissions (M5)

## Steps

1. [x] The candidates built behind switches or as the default, each measurable on its own: the identity-less
   permission read, several sidecars at once, the Graph usage report for the sizes
2. [x] Tests against the fake module and the fixtures for each path
3. [x] Live measurement (main session): the three candidates against the 620-mailbox tenant, timings into the Log
4. [x] The winner becomes the default, the hint names the floor where no candidate wins; docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange M2 bundle (041–044)
- 2026-10-29 — checked against the reference before coding: `Get-EXOMailboxPermission` documents a default
  parameter set with `-ResultSize <Unlimited>` alone, while a Q&A answer reports the cmdlet refusing to run without
  an identity ("Identity is a mandatory value"), so only the live round decides
  (https://learn.microsoft.com/powershell/module/exchangepowershell/get-exomailboxpermission); the mailbox usage
  report is `GET /reports/getMailboxUsageDetail(period='D7')`, a 302 to a CSV download (JSON only on beta), with
  `Reports.Read.All` and a reports reader role, 14 CSV requests per 10 minutes per app, and the user names
  concealed when the tenant's report privacy setting is on
  (https://learn.microsoft.com/graph/api/reportroot-getmailboxusagedetail,
  https://learn.microsoft.com/graph/throttling-limits)
- 2026-10-29 — built, each behind a switch with the old way the default until measured: `mailbox access --user
  --method piped|identityless|parallel [--sidecars N]` (the FullAccess read: the piped mailboxes as before; one
  `Get-EXOMailboxPermission -ResultSize Unlimited` without an identity; the piped mailboxes split into N shares,
  2 to 8, 4 by default, each on its own sidecar connected with the same token at once) and `mailbox size --method
  piped|parallel|report [--sidecars N]` (`report`: Graph's usage report joined by user principal name to the Admin
  API's mailbox list, both side by side, no sidecar; the footer names the report's day and the mailboxes not in
  it; a concealed report is exit code 9 with the setting in the hint). The envelope carries `method` (and
  `reportDate`, `notInReport`). The fake module lists every mailbox's entries without an identity; the Core tests
  hold each method's answer equal to the piped one's
- 2026-10-30 — measured live by the main session (620 mailboxes; the worktree's Release build; a fresh sign-in whose
  Graph token carries Reports.Read.All; each run alone, from a cold process). `mailbox access --user` for an account
  with 8 grants: piped 122 s, `identityless` exit 13 in 6 s ("Identity is a mandatory value to provide for running
  Get-ExoMailboxPermission"), 4 sidecars 41 s, 8 sidecars 37 s, the same 8 grants each time. `mailbox size --top 5`:
  piped 56 s, 4 sidecars 35 s, 8 sidecars 37 s, the usage report 16 s (its day 2026-10-27, three days old). The
  floors: `Get-EXOMailboxPermission` costs the service about 0.2 s per mailbox and `Get-EXOMailboxStatistics` about
  0.1 s, whatever the client does; split across sidecars the time stops falling past 4 (37 s against 41 s for the
  permissions, 37 s against 35 s for the sizes), so the service is the limit, not the processes; the one listing
  call does not exist in this module (3.10.1). The report's 16 s were the Graph report (a redirect to a download)
  and the Admin API's paged mailbox list side by side, not timed apart; the list then fetched whole `Get-Mailbox`
  objects and asks only for the table's fields (`$select`) now
- 2026-10-30 — the defaults from the measurements: `mailbox access --user` reads the FullAccess entries split across
  4 sidecars (`--method piped` keeps one, `--sidecars N` 2 to 8); `identityless` is gone from the switch and the
  fake module refuses an identity-less call as the real one does; `mailbox size` over every mailbox reads the usage
  report, whose day the footer names (a missing Reports.Read.All is exit code 8 with `--method parallel` in the
  hint), and one mailbox, or `--method piped|parallel`, reads the live size. The sidecar-only overloads the window
  uses stay piped

## Deviations from the plan

- The identity-less permission read was built and measured, then dropped: the module refuses it.
- `mailbox size` picks the way per question: the report for every mailbox, the live statistics for one mailbox,
  where one request is fast and today's size is what is asked.

## Verification

- 2026-10-30 — offline, in the worktree, on the bundle's tip after the merge of `main` (9b6214b): the whole local
  matrix green (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and
  without the front end with 0 warnings, `dotnet test` 599 tests, `Invoke-Pester -Path tests/sidecar` 27,
  PSScriptAnalyzer 0 findings, `changelog.py --check`, `check-fixtures.py` 250 files and 0 findings, the commit
  rules' self-test; with an empty `M365AEGIS_HOME` `doctor --offline` exit 0 and `whoami` exit 3; the window's
  `--smoke` exit 0; `release.ps1 -DryRun` green); the new test data through the anonymizer scan, which flagged only
  the made-up contoso and example addresses and number-shaped quotas
- 2026-10-30 — live, in the main session's round (00:15–00:45, the worktree's Release build before the defaults
  changed): the timings of the Log above, every run exit 0 except `identityless` (13); no pwsh process left behind
  after the parallel runs; `--method report` before the consent exited 8 `forbidden` in 6.6 s with Graph's
  `S2SUnauthorized` and the hint naming Reports.Read.All and `login`, and after it exit 0 in 16 s, backend
  `graph+admin-api`, 5 rows, the report's day 2026-10-27
- 2026-10-30 — re-checked live with the new defaults on the rebuilt Release build (00:52–00:54, the cached token):
  `mailbox size --top 5` without `--method` exit 0 in 16852 ms, backend `graph+admin-api`, method `report`, 5 rows,
  the footer "sizes from the mailbox usage report of 2026-10-27; 30 mailbox(es) not in the report yet" (30 of 620:
  the 18 room and 2 scheduling mailboxes are likely among them, which the usage report does not list, and mailboxes
  younger than the report); `mailbox access --user <upn>` without `--method` exit 0 in 50432 ms, backend `pwsh`, 4
  request ids, method `parallel`, 8 grants, 0 failed, 620 mailboxes scanned (41 s in the measurement; the service's
  time varies). Both answer the tenant under a minute; no sidecar left behind

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after #22)
- [x] moved to `done/` with `closed: 2026-10-30`
