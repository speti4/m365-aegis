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

1. [ ] The record carries each change's target object id (and the CSV line it came from) under a bumped record
       version; an old record reads as before (text match, the whole batch planned)
2. [ ] The resume plans only the rows of the pending changes (by CSV line, or by the arguments' targets), so 60
       pending of 100 cost 60 reads; the match by object id first, the text second
3. [ ] Core and Cli tests: the 100/60 case over the fixtures (a request count), a renamed target matched, an old
       record read
4. [ ] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes follow-ups bundle (064, 065, 066)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
