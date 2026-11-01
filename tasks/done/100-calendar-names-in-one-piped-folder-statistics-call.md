---
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
closed: 2026-11-01
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

1. [x] Measure first: how the take finds the calendar folder name today (which cmdlet, how many calls) and what the
       baseline take's folderPermissions domain costs (the main session's numbers)
2. [x] The piped read over the sidecar (the allow-list, the C# allow-list in `ExoCmdlets.cs` and the Pester tests if a
       pipeline needs a new request shape), the domain's `read` label names the piped form
3. [x] The fake module and the take fixtures give the same calendar rows; the Core tests and the sidecar Pester tests hold it
4. [x] The live measurement of both reads in the Verification; docs under *Docs that move*; `tasks_check` without
       `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session
- 2026-10-31 — measure first: the take named each calendar through `MailboxLookups.FolderPermissionsAsync` without a
  folder, so `CalendarPathAsync` sent one `Get-EXOMailboxFolderStatistics -Identity <mailbox> -FolderScope Calendar` per
  mailbox before the Admin API's `Get-MailboxFolderPermission`: over the fixtures 6 sidecar and 6 Admin API calls for 6
  mailboxes; the live baseline is in the Verification
- 2026-10-31 — `MailboxLookups.CalendarPathsAsync`: `Get-EXOMailbox` (with `ExchangeGuid`), then the mailboxes' object
  ids piped into one `Get-EXOMailboxFolderStatistics -FolderScope Calendar` over the sidecar's existing `input`
  (`-Identity` takes pipeline input by value, as the cmdlet's docs say), so no new request shape and the allow-list is
  unchanged (D-004). A folder is matched to its mailbox by `ContentMailboxGuid`/`MailboxGuid` (the mailbox's
  `ExchangeGuid`), else by its `Identity` up to the first backslash, a name one mailbox alone answers to; the shortest
  `Calendar`-type path wins, as before. A mailbox the piped read does not name is read one by one as before, and the
  domain's note counts those, so the live take shows whether the matching held. Over the fixtures: 2 sidecar calls, the
  same calendar rows. The fake module's folder statistics now carry `Identity` and `ContentMailboxGuid`; a Pester test
  pipes three identities (one unknown) in one call; `CalendarPathsTests` hold the identity match, an ambiguous name, a
  folder no mailbox owns, a mailbox without a calendar, no mailbox, a failed read (empty answer, logged) and a refused
  sign-in
- 2026-11-01 — merged in #151 (squash); closed on `main` by the main session: of the pull request's follow-ups, the parallel read of the calendar permissions is task 117

## Deviations from the plan

- The piped read pipes the mailboxes' object ids through the sidecar's existing `input` instead of a new
  `Get-EXOMailbox | ...` pipeline request: the same documented pipeline, no change to the allow-list or the protocol
- One sidecar runs the piped read, not a split across sidecars as the grants read does; the live numbers decide whether
  a split is worth a follow-up

## Verification

- 2026-11-01 — live, the main session, cached token (tenant counts rounded): the folderPermissions domain of the
  baseline take (build `5a30bc7`, one calendar-name call per mailbox) took 364,632 ms with some 620 Admin API calls
  and some 620 sidecar calls, about 590 ms per mailbox for the pair; the after-take (build `46118d7`) took 227,317 ms
  with some 620 Admin API calls and 3 sidecar calls: `Get-EXOMailbox`, the piped folder statistics, and one mailbox
  the piped read did not name, read on its own (the note says so). The rows stayed at some 1,500. The rest is the
  Admin API's `Get-MailboxFolderPermission`, one per mailbox, which this task leaves as it is
- 2026-11-01 — local, merged tree: the matrix of task 097's Verification, the sidecar Pester tests included

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposed them and filed the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-11-01` by the main session after the merge
