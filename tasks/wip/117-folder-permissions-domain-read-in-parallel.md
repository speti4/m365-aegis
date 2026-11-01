---
size: S
origin: wip/100-calendar-names-in-one-piped-folder-statistics-call.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 117 — The folderPermissions domain of the take reads the calendar permissions in parallel

## Context

The per-mailbox `Get-MailboxFolderPermission` reads over the Admin API run up to eight at a time through an adaptive
gate that halves on a throttled answer and grows back after a round without one; the take's Admin API client gets the
take's own throttling budget (8 retries, an hour of waits in all, as the Graph client has had since task 087) instead
of a command's (4 retries, a minute); and the `requests` column shows the Admin API's waits on throttling next to its
calls, as it shows Graph's.
Why: after tasks 097, 099 and 100 the calendar permissions are most of the incremental take (about 3.8 minutes of 7.5
over some 600 mailboxes in the live run of 2026-11-01, about 1.45 s per call at four in flight). The reads were four at
a time already; what the take lacked was a budget sized for 600 calls, any sign of throttling in its output, and a
concurrency that backs off instead of failing items once the minute is spent.
Done when: the fake module's take gives the same rows and the same call counts, the live domain time drops measurably
without a throttled answer (a before/after in the Verification), and the `requests` column of the take still counts
every call.
Decided 2026-11-01: an adaptive gate with a constant ceiling of eight, no new profile setting; the live measurement
decides whether a setting is worth a follow-up.
Fourteenth round of 2026-11-01, worked in the main session on its own branch (no worktree, no task session).

## Docs that move

- `docs/development.md`, the snapshot domains table (`folderPermissions`: "four at a time" becomes the gate) and the
  throttling paragraph after it (the Admin API client's take budget, the waits in the `requests` column)

## Not in this change

- A profile setting for the concurrency ceiling (a follow-up if the live measurement asks for one)
- The grants domain's split across sidecars and its fixed four
- The calendar names' piped read (task 100) and the holder matching (task 101)

## Steps

1. [ ] `AdminApiClient.WithBudget` on the Graph client's pattern: shared counters (`Calls`, `Waited`,
       `ThrottledAnswers`) across the budgeted copy; `Waited` and `ThrottledAnswers` public
2. [ ] `ExchangeBackends.BudgetAdminApi` (the budgeted client under the counting wrapper), `AdminApiWaited`,
       `AdminApiThrottled`; the take calls it next to `Graph.WithBudget`
3. [ ] `AdaptiveGate` (ceiling, halve on a throttled release, +1 after a clean round, never below one) and the
       folderPermissions domain's reads through it, `SnapshotTakeOptions.AdminApiParallel = 8`; the domain's remark
       names a back-off when one happened
4. [ ] `SnapshotDomain.AdminApiThrottledMs` from the client's waits per domain; `snapshot take`'s `requests` column
       prints `N admin-api (throttled X s)`
5. [ ] Tests: the budgeted client's shared counters and a 429 past a command's minute; the gate's bounds; a throttled
       calendar read in the take (the Graph test's Admin API twin); an in-flight probe (at most eight, half after a
       throttled answer); the take's rows and counts unchanged; the table's new form
6. [ ] Docs under *Docs that move*; the local matrix; the live before/after in the Verification

## Log

- 2026-11-01 — promoted on `task/117-folder-permissions-domain-read-in-parallel` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
