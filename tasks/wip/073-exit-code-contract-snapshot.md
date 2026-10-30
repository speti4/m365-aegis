---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 073 — An exit code contract snapshot: the `ExitCode` enum's names and values, frozen

## Context

A Verify snapshot of the `ExitCode` enum as `name = value` lines, one per member, plus a check that the exit code
table in `docs/development.md` lists the same pairs, so a renumbering, a renamed member or an undocumented
addition fails a test instead of a reader.
Why: the output contract (D-006) promises that exit codes never change meaning; a script or an agent branches on
them, and the parallel-session rounds already added codes on separate branches (8, 9, 70).
Done when: the snapshot is accepted, the docs check is green, and changing a value in the enum turns the test red.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. A new exit code in this round is handed out by the main session; the
snapshot then changes with it, which is the point.

## Docs that move

- `docs/development.md`, "The output contract": one sentence naming the snapshot and the docs check (the table
  itself is read by the test, so its shape is now a contract too)

## Not in this change

- Any change to the codes or their meaning

## Steps

1. [ ] The snapshot test in the Core tests (`Snapshots/`), one `name = value` line per member, in value order
2. [ ] The docs check: the exit code table in `docs/development.md` parsed and compared to the enum, both ways
3. [ ] The proof that a changed value turns the test red (a Log line, the change not committed)
4. [ ] The docs sentence

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)

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
