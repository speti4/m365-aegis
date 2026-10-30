---
size: S
origin: done/051-plan-type-plan-records-and-the-write-guard.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 060 — `plan list` and `plan show` over the plan records

## Context

`plan list` reads the records under the home folder's `plans\` (task 051) into a table (when, operation, mode,
changes applied and failed) and `plan show <record>` prints one record as the plan rendering with each change's
result; both read-only, both with the envelope.
Why: the records are the audit trail of every apply, and today only a file browser reads them.
Done when: a temp home with three records lists them newest first, `plan show` renders a partial apply with its
failed change, and a missing record is exit 4.
Part of the writes and plan records bundle (058, 061, 060, 062) of the round of 2026-10-30; the task session owns
the steps below and may reshape them. The live line is a `plan list` and a `plan show` over the real home's records
of round 4, read-only, from the main session. The parallel Exchange and window bundle does not touch `PlanStore` or
the `plan` command.

## Docs that move

- `docs/development.md`, "Writes and plans": the two commands over the records
- `README.md`: the bundle's own heading

## Not in this change

- a delete or a prune of the records
- the window's record browser

## Steps

1. [x] `PlanStore` reads the records (newest first, a record by its name or its full path, a missing one exit 4)
2. [x] `plan list` (the table and the envelope) and `plan show <record>` (the plan rendering with each change's
   result) in the Cli, no sign-in, backend `local`
3. [x] Core and Cli tests over a temp home with three records, the snapshots of the two renderings
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes and plan records bundle (058, 061, 060, 062)
- 2026-10-30 — `Core/Operations/PlanStore.cs`: `PlanStore.List(home)` (every `*.json` under `plans`, newest first by the
  apply's start, then by name; a file that does not read as a record is counted in `unreadable`, not listed; an empty
  list when the folder does not exist) as `PlanRecordList` of `PlanRecordSummary` (name, operation, mode, title,
  changes, applied, failed, skipped, started, finished, profile, the record it resumes); `PlanStore.Open(home, record)`
  by the file name with or without `.json`, or by a path: a missing record `not-found` (4), a file that is not a plan
  record `usage` (2). A record also keeps a CSV batch's file and text (`input`, task 062).
- 2026-10-30 — `Commands/PlanCommand.cs`: `plan list` (the table: started (UTC), operation, mode (`cut short` for a
  record left at `applying`), changes, applied, failed, skipped, record; the folder, the files left out and the next
  commands under it) and `plan show <record>` (`PlanTableView`'s rows and footer, then the source, the times, the
  profile, the arguments and the kept file; the envelope's data is the record as the file holds it), both through
  `CommandRunner.Run` with backend `local`, no sign-in. `PlanTableView` writes `no result` for a change without one (a
  cut-short record) and a `resumes:` line under a resumed plan.
- 2026-10-30 — tests: `tests/shared/PlanRecordSamples.cs` writes three records through `PlanStore` (an owner added in
  full, 2026-10-28; a CSV batch of three grants with line 3 failed, 2026-10-29; a member write to Finance stopped by a
  sign-in failure, 1 applied, 2 failed, 3 skipped, 2026-10-30), linked into both test projects; `PlanRecordsTests`
  (the list newest first with the counts and a foreign file counted, an empty home, a record by its name with and
  without the extension and by its path, a missing record and a foreign file), `PlanCommandTests` (the envelope of
  `plan list`, an empty home, `plan show` of a missing record exit 4 with backend `local`, `plan show` as the file holds
  it, and the snapshot `PlanCommandTests.TheTablesOfPlanListAndPlanShow` of the list and two records).
- 2026-10-30 — docs: `docs/development.md` "Writes and plans" ("The records read back", the record's `input`),
  `README.md` (the bundle's heading).

- 2026-10-30 — closed by the main session on `main` after #26; the follow-up (`plan prune` or a size limit on the plans folder) is filed as backlog item 066

## Deviations from the plan

- A file in the plans folder that is not a plan record is left out of `plan list` (counted under the table), and
  `plan show` of such a file is a usage error (2): no new exit code was needed.
- `plan list`'s mode column says `cut short` for a record left at `applying`, the one state a reader has to notice.

## Verification

- 2026-10-30 — the local matrix in the worktree, as in task 058's Verification; in the empty home of the `windows`
  job's steps `plan list` answered an empty list, exit 0, backend `local`.
- 2026-10-30 — over the three sample records: `plan list` newest first (the stopped write 6 changes, 1 applied, 2
  failed, 3 skipped; the batch 3, 2, 1, 0; the owner 1, 1, 0, 0), `plan show` of the batch with line 3 `failed:
  cmdlet-failed` and the service's message, `plan show 20200101T000000.000Z-nothing` exit 4.
- 2026-10-30 — live, the main session's real home: `plan list` exit 0, backend `local`, `records: []`, 4 ms;
  `plan show 20200101T000000.000Z-nothing` exit 4 `not-found`, the message naming the home's `plans` folder. With a
  record made from a dry run's plan (two changes marked failed, nothing applied): `plan list` exit 0, 1 record
  (`group-members-add`, `applied`, 0 applied, 2 failed, 0 skipped); `plan show <record> --table` exit 0, both rows
  `failed: throttled`, the two `failed (…)` lines, `applied 0 of 2 change(s); the plan record is <path>`, the source,
  times, profile and arguments lines. The record was removed after the check.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
