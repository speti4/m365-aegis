---
size: S
origin: done/060-plan-list-and-plan-show-over-the-records.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 066 — `plan prune`, or a size limit on the plans folder

## Context

`plan prune --older-than <days>` (behind the usual `--dry-run`/`--yes` guard) removes old plan records from the
home folder's `plans\`, or the folder keeps a documented size limit and the oldest records go when an apply
writes a new one; `plan list` says how many records there are and how much they take.
Why: since task 062 a record keeps a CSV batch's full text, so the folder grows with every bulk apply and nothing
trims it.
Done when: a temp home with records older and newer than the limit keeps the newer ones, the dry run lists what
would go, and `docs/development.md` names the rule.
Part of the writes follow-ups bundle (064, 065, 066) of the sixth round of 2026-10-30; the task session owns the
steps below and may reshape them, and chooses between the command and the limit (or both) with the reason in
the Deviations. A prune is a local write (no tenant), so the plan it prints is a plan over files; the guard
flags keep their meaning. The live line is a `plan list` on the real home from the main session (the count and
the size only).

## Docs that move

- `docs/development.md`, "Writes and plans" (the plan records): the prune, or the limit, and `plan list`'s totals
- `README.md`: the bundle's own heading

## Not in this change

- Pruning the logs folder (its own rule, if any, is another task)
- A record format change (task 065's)

## Steps

1. [x] `plan list` reports the count of the records and their size (`count`, `bytes`; the table's footer)
2. [x] `plan prune --older-than <days>` behind `--dry-run`/`--yes`, a `Plan` over the records to remove, the exit codes
       as every write; no automatic size limit (Deviations)
3. [x] Core and Cli tests over a temp home with older and newer records
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes follow-ups bundle (064, 065, 066)
- 2026-10-30 — `PlanRecordList` gains `Count` and `Bytes` (every file in the folder, records and others);
  `PlanListTableView`'s footer says `N plan record(s) under <folder>, <size> on disk` and names `plan prune`.
- 2026-10-30 — `Core/Operations/PlanPrune.cs`: `PlanPrune.Plan(home, days, now)` builds a `WritePlan` of one
  `remove` change per record started before `now - days` (the record's `started`, else the file's time), oldest first:
  the file name as the target, the operation, start, change count and size as the change, a warning per record that
  left changes to do (its resume goes with it); a file that is not a record is never touched; `days` below 1 is a
  usage error, so an apply running now is never removed. A file the file system refuses fails its change as
  `forbidden` (exit code 8), and the guard goes on. The Cli's `plan prune --older-than <days>` (required) runs it
  through `PlanGuard` with `WriteFlags` (no `--resume`), backend `local`; the apply's own record (`plan-prune`) stays.
- 2026-10-30 — tests: `PlanPruneTests` (the totals; a dry run of 33 days a month later lists the 28th only and removes
  nothing; an apply of 32 days removes the 28th and the 29th, keeps the 30th, the foreign file and its own record, and
  warns for the batch with a failed line; nothing older is up to date with no record; 0 and -3 days refused), the
  Cli's `PlanListCountsTheRecordsAndTheirSize`, `PlanPruneIsAWriteOverTheLocalRecords` (exit 7 without a flag, the dry
  run, the apply, counted against today) and `PlanPruneIsRefusedBeforeAnythingIsRemoved` (no `--older-than`, 0, both
  flags, `--resume`: exit 2, nothing removed). The table snapshot of `plan list` scrubs the size, which varies with
  the temporary folder's path length.
- 2026-10-30 — docs: `docs/development.md` (the bundle's own heading: the totals, the prune and why no limit),
  `README.md` (the bundle's heading).

## Deviations from the plan

- The command, not a size limit: an automatic limit at apply time would remove records without a plan anyone
  confirmed, which D-006 rules out for every write, and the records are the audit trail and the source of `--resume`
  (a size limit is blind to whether a record still has changes to continue). `plan list` shows the size, so the
  administrator decides when to prune.
- `--older-than` is at least 1 day, so a record of an apply running at that moment is never in the plan.
- A refused removal (a locked file, a denied folder) uses the existing `forbidden` kind (exit code 8) rather than a
  new exit code: the file system refused, as a service refuses a permission.
- The prune's own apply is recorded like any write (`<time>-plan-prune.json`), so what was removed stays visible.

## Verification

- 2026-10-30 — the local matrix in the worktree, as in task 064's Verification (833 .NET tests, 0 failed).
- 2026-10-30 — the exe on an empty home: `plan list --table` exit 0, `0 plan record(s) …, 0 B on disk`; `plan prune
  --older-than 30 --table` exit 0, up to date, nothing sent.
- 2026-10-30 — live, the main session's signed-in round on the real home: `plan list --json` exit 0 in 117 ms, backend
  `local`, `count` 0 and `bytes` 0 (the home held no record), data keys `bytes,count,folder,records,unreadable`; the
  table's footer `0 plan record(s) under <home>\plans, 0 B on disk, newest first`; `plan prune --older-than 1
  --dry-run --json` exit 0 in 122 ms, `plan-prune`, mode `up-to-date`, 0 changes, 0 warnings, and `--older-than 3650`
  the same; the table's title `… (older than 1 day(s)): 0 of 0`; 0 records on disk before and after. The non-empty
  cases (older and newer records, the warnings, the apply) are verified by the temporary-home tests above.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (the D-004 note's text is in the pull request; the main
      session writes it on `main`)
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
