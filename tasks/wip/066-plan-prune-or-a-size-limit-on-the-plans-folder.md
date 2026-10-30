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

1. [ ] `plan list` reports the count of the records and their size on disk (the envelope and the table)
2. [ ] `plan prune --older-than <days>` behind `--dry-run`/`--yes`, a `Plan` over the records to remove, the
       exit codes as every write; or the documented size limit at apply time (the session decides)
3. [ ] Core and Cli tests over a temp home with older and newer records
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
