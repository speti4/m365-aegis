---
size: S
origin: done/062-resume-a-half-applied-batch-from-its-record.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 065 — A resume plans only the pending lines and matches them by object id

## Context

`--resume <record>` (task 062) plans only the lines of the batch whose change the record left failed or skipped
(today it plans the whole batch again and keeps the pending part), and matches a pending change to the new plan
by the target's object id kept in the record instead of the rendered text (today a rename between the apply and
the resume, or a record edited by hand, shows as a warning instead of a match).
Why: a 100-line batch with 60 lines left reads 100 objects today; and the text match is brittle, as the live
check of 2026-10-30 showed with an encoding slip.
Done when: a record of 100 lines with 60 pending resumes with 60 reads, a renamed target still matches, and the
record format carries the ids with a version bump the old records still read under.
Part of the writes follow-ups bundle (064, 065, 066) of the sixth round of 2026-10-30; the task session owns the
steps below and may reshape them. The safety tests bundle (071–075) runs in parallel and adds CSV edge-case tests
over `CsvRows`; this task changes the resume's narrowing and the record, and touches `CsvRows.cs` only if the
pending-lines plan needs a row filter there (named under Deviations).

## Docs that move

- `docs/development.md`, "Writes and plans" and "A resume from a plan record": the pending-lines plan, the ids in
  the record, the record version and what an old record still gives
- `README.md`: the bundle's own heading

## Not in this change

- A resume of a `call` record
- An edit of the record's changes before the resume

## Steps

1. [x] The record carries each change's row place (`index`) and object ids (`key`), and each unchanged object's,
       under record version 2; an old record reads as before (text match, the whole batch planned)
2. [x] The resume plans only the rows of the pending changes (`PlanResume.Select`, by the row's place among the
       arguments or the CSV lines), so 60 pending of 100 cost 60 reads; the match by object ids first, the CSV line
       second, the text third
3. [x] Core and Cli tests: the 100/60 case (a read count), a renamed target matched, an old record read
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes follow-ups bundle (064, 065, 066)
- 2026-10-30 — the model: `PlanChange` and `PlanUnchanged` gain `Index` (the row's place among the write's rows, from 1,
  for the arguments and the CSV lines alike) and `Key` (object ids joined with `/`: `<group id>/<user id>`,
  `<mailbox id>/<right>/<holder id>`, `<mailbox id>` for the forwarding; compared without case). A new interface
  `IPlanRow` (`Row`, `Index`) is implemented by `GroupChangeRow`, `AccessChangeRow` and `ForwardingRow`, whose
  factories (`FromArguments`, `FromCsv`, `AccessRows`, `AccessRowsFromCsv`, `ForwardingRows`, `ForwardingRowsFromCsv`)
  number the rows; `PlanRows.IndexOf` leaves an unnumbered row's place null. `PlanStore.RecordVersion` is 2.
- 2026-10-30 — the resume: `PlanResume.Select<T>(rows)` keeps the rows of the pending changes when the record is of
  version 2 and every pending change names its place, else every row; `WriteRunner.TryRows` calls it under a resume,
  so no write command changed. `Narrow` keeps a fresh change whose action and key match a pending one, then by the
  CSV line, then by the text; an unchanged object by its key, line or text; `Continued` checks the key first.
- 2026-10-30 — tests: `PlanResumeRowsTests` (a record of 100 additions to Finance, 40 applied, 60 failed: the resume
  selects rows 41 to 100, reads exactly those 60 users in three `$batch` calls over a transport that answers them and
  counts them, and keeps 60 changes with no warning; a group and a user renamed since the apply still match by their
  ids; a version 1 record plans every row and its pending change of the old names is a warning; a version 2 record
  without the places plans every row; the rows' numbering), the Cli's `AResumeOfAVersionTwoBatchPlansOnlyItsPendingLines`
  (a CSV batch whose applied line names a user deleted since: the resume plans Dave's line alone, exit 0) and
  `AResumeOfAVersionOneBatchPlansEveryLineAgain` (the same record as version 1: the whole batch, the deleted user's
  404, exit 4). `PlanRecordSamples` gains `Record` (returns the file name), `AsVersionOne` and `WriteBatch`;
  `PlanGuardTests` asserts `PlanStore.RecordVersion`.
- 2026-10-30 — docs: `docs/development.md` (the records' version in "Writes and plans"; the bundle's own heading),
  `README.md` (the bundle's heading).

## Deviations from the plan

- `CsvRows.cs` is untouched: the row filter works on the place each row factory numbers (`IPlanRow.Index`), not on the
  CSV reader, so arguments and files narrow the same way.
- The row's place is recorded beside the CSV line (`index` beside `row`): the arguments have no line, and one place for
  both keeps the filter in one spot (`WriteRunner.TryRows`) instead of in every write command.
- The object ids are one key per change (`key`) rather than a target id alone: a mailbox right needs the mailbox, the
  holder and the right to name one change, so the key carries the ids each write needs.
- `index` and `key` show in a write's envelope too (the plan is the envelope's data and the record's plan), additive
  and null-omitted; a script can match a change to its objects by them.
- A record of version 2 whose pending changes carry no place (one written by a caller that does not number its rows)
  plans every row, as version 1 does.

## Verification

- 2026-10-30 — the local matrix in the worktree, as in task 064's Verification (833 .NET tests, 0 failed).
- 2026-10-30 — the 100/60 case: 60 user reads in 3 `$batch` calls for a resume of 60 pending changes of 100, rows 41
  to 100 kept, 60 changes and no warning (`PlanResumeRowsTests`).
- 2026-10-30 — live, the main session's signed-in round (dry runs only): the plans of task 064's dry runs carry `index`
  and `key` on each change; the real home held no plan record, so no resume was run against the tenant; the narrowed
  resume and the version 1 record are verified by the tests above.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (the D-004 note's text is in the pull request; the main
      session writes it on `main`)
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
