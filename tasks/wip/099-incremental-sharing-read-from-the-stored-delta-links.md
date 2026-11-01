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

1. [x] The previous complete snapshot found (the newest with the sharing domain complete), its `links`, `linkGrantees`
       and each drive's `deltaLink` read into the take
2. [x] Per drive: the delta from the stored link, the changed items' permissions re-read, the removed items dropped;
       no stored link or `resyncRequired` (or any error of the delta call) falls back to the full read; `--full` forces
       the full read; the manifest names the incremental drives
3. [x] Fixtures: a second delta page with one changed and one removed item; the Core tests hold that the second take
       reads that item's permissions only and gives the same rows as a full take
4. [x] The live second take on an unchanged day (the main session's numbers) in the Verification; docs under *Docs
       that move*; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session
- 2026-10-31 — the rule for the snapshot it starts from (`SharingBase.Load`): the newest readable snapshot of the
  profile whose `sharing` domain is `ok`, or `partial` with the new `deltaLinks` = `whole-drives` manifest marker. A
  take stores a drive's delta link only when the drive was read whole, so a partial sharing domain still serves its
  whole drives and the throttled or refused drive is read whole next time. A snapshot taken before this change with a
  partial sharing domain (such as the live baseline, partial by one throttled drive) stored links for every drive and
  cannot tell which one failed, so it is passed over; an older `ok` one still serves
- 2026-10-31 — per drive: the delta from the stored link (`Prefer: hierarchicalsharing`); an item's last occurrence is
  its state; `deleted` or no `shared` facet drops the item's rows, a `shared` one has its permissions read again, and
  the drive's other `links`/`linkGrantees` rows come from the previous snapshot, the grantees matched to this take's
  objects. No link, a `410 resyncRequired` or any other error of that delta (but the sign-in's) reads the drive whole;
  `snapshot take --full` reads every drive whole. The manifest names `sharingBase` and `incrementalDrives`; the note
  counts the incremental drives, the changed items and the drives that fell back. No table or column changed, so the
  schema stays at 1 and D-011 needs no note
- 2026-10-31 — fixtures: `delta@token=delta-finance-1.json` (Budget changed, Reports removed) and
  `delta@token=delta-phoenix-1.json` (nothing changed). `SnapshotTakeTests`: a second take over the fixture snapshot
  reads Budget's permissions only, no drive whole, and gives the full take's rows without Reports; `--full` reads both
  drives whole. `SnapshotEdgeTests`: over a scripted tenant an incremental take gives exactly the rows of a `--full`
  take of the changed state (a link changed, an item removed, one newly shared, one item twice in the delta) and reads
  a drive whose delta answers 410 whole; the base rule passes over an unreadable file, a snapshot whose sharing failed
  and a partial one without the marker, and uses a partial one with it, its failed drive read whole
- 2026-11-01 — live order of the takes: the baseline snapshot (build `5a30bc7`) has a partial sharing domain (one
  throttled drive) and no `deltaLinks` = `whole-drives` marker, so the rule passes it over as a base; the after-take
  ran with `--full` (a measured whole read, and no older `ok` snapshot could serve), and the incremental take started
  from it
- 2026-11-01 — merged `origin/main` (the round's other three bundles) into the branch; the conflict in the Cli's
  snapshot command (`diff` next to `--full`) kept both

## Deviations from the plan

- "The previous complete snapshot" became "the newest snapshot whose sharing was read, with only its whole drives
  trusted": a nightly take is often partial by a throttled drive, and requiring an `ok` domain would rarely leave an
  incremental take
- The delta reads with `Prefer: hierarchicalsharing` only; `deltashowsharingchanges` needs `Sites.FullControl.All`,
  which D-011 declined, so a periodic `--full` take stays the safety net (a follow-up)

## Verification

- 2026-11-01 — live, the main session, cached token, build `46118d7` (tenant counts rounded): the full sharing read
  (`snapshot take --full`) took 1,086,649 ms with 500 Graph requests, 887,415 ms of it waiting on `Retry-After`, ok,
  some 21,000 rows. The incremental take right after it took 4,827 ms with 108 Graph requests and no throttling, ok,
  the same order of rows; its note said every drive (about a hundred) was read incrementally from the after-take,
  26 changed items, and no drive fell back to a whole read. That is about 225 times faster, two orders of magnitude.
  The whole take went from 24.2 to 7.5 minutes, the calendars' Admin API reads now most of it
- 2026-11-01 — live: `snapshot diff <after-take> latest` (task 098's diff) between the full and the incremental take
  found no change in members, owners, sharing, grants, folderPermissions or forwarding, only the two forbidden
  sites both takes share: the changed items read again gave the same sharing rows
- 2026-11-01 — local, merged tree: the matrix of task 097's Verification

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
