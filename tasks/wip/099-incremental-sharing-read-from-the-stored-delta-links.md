---
size: M
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 099 — An incremental sharing read from the delta links the previous snapshot stored

## Context

The sharing domain of the take starts from the previous complete snapshot's `links` and `linkGrantees` rows and
each drive's stored `deltaLink` (the manifest of task 087), reads only the items the delta reports as changed, refreshes
their permissions and drops the removed ones; a drive without a stored link, or whose delta answers `resyncRequired`,
falls back to the full read; `--full` forces it.
Why: the full sharing read is the take's longest part over the tenant (about 20 minutes of the 24.6-minute live take of
2026-10-31, most of it the permission calls under throttling); a nightly run changes little.
Done when: over the fixtures a second take after one changed item reads that item's permissions only and gives the same
rows as a full take, the manifest says which drives were incremental, and the live take's sharing time drops by an
order of magnitude on an unchanged day.
Eleventh round of 2026-10-31, on one branch with tasks 097, 100 and 101; the task session owns the steps below and may
reshape them. The live second take on an unchanged day is the main session's.

## Docs that move

- `docs/usage.md`: `snapshot take --full` and the incremental read's rule (which snapshot it starts from, when it
  falls back)
- `docs/development.md`: the manifest's incremental fields, if the snapshot section describes the manifest
- `tasks/decisions/D-011-snapshot-store-format.md`: a note only if the store's format changes (a new column or table)

## Not in this change

- An incremental read of any other domain (groups, members, grants): full reads, as today
- The retention rule (D-011): the previous complete snapshot is read, never kept longer for this

## Steps

1. [ ] The previous complete snapshot found (the newest with the sharing domain complete), its `links`, `linkGrantees`
       and each drive's `deltaLink` read into the take
2. [ ] Per drive: the delta from the stored link, the changed items' permissions re-read, the removed items dropped;
       no stored link or `resyncRequired` (or any error of the delta call) falls back to the full read; `--full` forces
       the full read; the manifest names the incremental drives
3. [ ] Fixtures: a second delta page with one changed and one removed item; the Core tests hold that the second take
       reads that item's permissions only and gives the same rows as a full take
4. [ ] The live second take on an unchanged day (the main session's numbers) in the Verification; docs under *Docs
       that move*; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the sharing domain's time and permission call count of a full and of an incremental live take>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
