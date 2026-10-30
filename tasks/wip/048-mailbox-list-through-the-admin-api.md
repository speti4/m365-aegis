---
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M2 Exchange
---

# 048 — `mailbox list` through the Admin API, and the usage report timed apart from the mailbox list

## Context

`mailbox list` reads the paged `Get-Mailbox` of the Admin API (every field the list shows, `$select` for the
table's columns) instead of starting a sidecar; and the `report` method of `mailbox size` times Graph's usage
report and the Admin API's mailbox list apart, so the Log says which of the two costs the 16 s measured on
2026-10-30 (the two ran side by side, unmeasured).
Why: the list is the most common mailbox read and pays the 2.4 s cold connect for nothing the REST layer cannot
give; the size default's time is unexplained until the two calls are measured apart.
Done when: `mailbox list` answers from fixtures and live with backend `admin-api` and no pwsh child, the two
timings are in the Log, and `docs/development.md`'s layer table names the list under the Admin API.
Part of the Exchange and window follow-ups bundle (050, 049, 048, 046, 047) of the round of 2026-10-30; the task
session owns the steps below and may reshape them. The live lines (the backend, the pwsh child count, the two
timings) come from the main session's signed-in round. The parallel M3 bundle (051–054) adds write cmdlets to the
sidecar's allow-list and write commands under `mailbox`; this task changes the reads only.

## Docs that move

- `docs/development.md`, "The Exchange Online sidecar": the layer table row of `mailbox list`, the `report` method's two timings

## Not in this change

- `mailbox forwarders` on the Admin API (a follow-up if the list's path proves it)
- the sidecar's own `Get-EXOMailbox` list (stays as the fallback when the Admin API cannot answer)

## Steps

1. [x] `mailbox list` on the Admin API's `Get-Mailbox` with `$select`, the sidecar as the fallback; `--shared` kept
2. [x] The `report` method of `mailbox size` times the usage report and the mailbox list apart (verbose log or `meta`)
3. [x] Recorded Admin API answers for the list in the fixtures, scrubbed; Core and Cli tests, snapshots
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window follow-ups bundle (050, 049, 048, 046, 047)
- 2026-10-30 — `MailboxLookups.ListAsync(ExchangeBackends, …)`: the Admin API's `Get-Mailbox -ResultSize Unlimited`
  with `$select` on the list's eight fields (the `MailboxProperties` the report's join already reads), over every
  page, no sidecar. The `Mailbox` endpoint documents `Identity`, `ResultSize` and
  `IncludeGrantSendOnBehalfToWithDisplayNames` only (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-mailbox,
  read 2026-10-30), so `--shared` keeps the `SharedMailbox` rows of the whole list instead of passing
  `-RecipientTypeDetails`. A failure other than the sign-in (`auth`) or a usage error falls back to the sidecar's
  `Get-EXOMailbox` with a verbose `admin-api` line; a refused sign-in fails as it is, since the sidecar would ask the
  same token. The sidecar-only overload stays for the window's `mailbox-list` (045) and the tests.
- 2026-10-30 — the request is the report's own, so the recorded answer of task 043 (two pages) answers it; no new
  fixture. The report method wraps its two reads in a timer from their common start and writes, under `--verbose`,
  `mailbox size, report method: Graph's usage report answered after N ms` and the same for the Admin API's (or the
  sidecar's) mailbox list.

## Deviations from the plan

- no new fixture: the list's request is byte for byte the report's mailbox list of task 043, whose recorded pages answer it
- the timings go to the verbose log, not to `meta` (the envelope's shape stays as it is)

## Verification

- 2026-10-30 — Core tests (`MailboxListLayersTests`): the list from the Admin API's two pages, 6 mailboxes, `--shared`
  2, backend `admin-api`, `sidecar.Starts` 0, no `RecipientTypeDetails` in the body, `$select` in the URL; the same
  rows as the sidecar's list; a 403 or a 500 leaves the list to the sidecar (`admin-api+pwsh`); a 401 fails as `auth`
  with no sidecar started. Cli tests (`MailboxListCommandTests`): `--json mailbox list` over the fixtures exits 0 with
  backend `admin-api` and 6 (or with `--shared` 2) mailboxes; without an Admin API it is `pwsh`; the table snapshot.
  The command line's existing snapshots unchanged. `dotnet test`: 645 passed, 0 failed.
- 2026-10-30, live (the main session's signed-in round, the broker's cached token): `mailbox list --json --verbose`
  exit 0, backend `admin-api`, no pwsh child, 621 mailboxes in 7.5 s; `--shared` backend `admin-api`, no pwsh child,
  219 shared mailboxes in 6.3 s. `mailbox size --top 5` (the report method): backend `graph+admin-api`, no pwsh
  child, 6.5 s; Graph's usage report answered after 1875 ms, the Admin API's mailbox list after 6504 ms (report day
  2026-10-27, 5 sizes, 30 mailboxes not in the report yet). The slow half of the report method is the mailbox list,
  not the usage report; the 16 s of 2026-10-30 (task 043) are 6.5 s now.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
