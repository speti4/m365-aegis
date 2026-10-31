---
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 100 — The calendars' folder names in one piped `Get-EXOMailboxFolderStatistics` call

## Context

The folderPermissions domain of the take finds each mailbox's calendar folder name (the mailbox's language names
it) from one piped call over every mailbox (`Get-EXOMailbox | Get-EXOMailboxFolderStatistics -FolderScope Calendar`)
instead of one call per mailbox, measured live before the change.
Why: one sidecar call per mailbox, some 600 a take, likely the slowest Exchange part of the live take of 2026-10-31;
the folder names change rarely and come from one cmdlet.
Done when: the live measurement of the per-mailbox and the piped read is in the Verification, the fake module's take
gives the same calendar rows, and the domain's `read` label names the piped form.
Eleventh round of 2026-10-31, on one branch with tasks 097, 099 and 101; the task session owns the steps below and may
reshape them. The live measurements (the per-mailbox baseline before the change, the piped read after) are the main
session's, over the broker's cached token.

## Docs that move

- `docs/development.md`, the sidecar section, if the piped form needs a new shape of request on the allow-list
- `docs/usage.md` or `docs/development.md`: the take's folderPermissions domain line, if either names the per-mailbox read

## Not in this change

- The calendar permission read itself (`Get-MailboxFolderPermission` over the Admin API, task 042), one per mailbox
- A folder other than the calendar

## Steps

1. [ ] Measure first: how the take finds the calendar folder name today (which cmdlet, how many calls) and what the
       baseline take's folderPermissions domain costs (the main session's numbers)
2. [ ] The piped read over the sidecar (the allow-list, the C# allow-list in `ExoCmdlets.cs` and the Pester tests if a
       pipeline needs a new request shape), the domain's `read` label names the piped form
3. [ ] The fake module and the take fixtures give the same calendar rows; the Core tests and the sidecar Pester tests hold it
4. [ ] The live measurement of both reads in the Verification; docs under *Docs that move*; `tasks_check` without
       `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the per-mailbox read's time and call count against the piped read's>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
