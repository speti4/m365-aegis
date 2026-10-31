---
status: idea
size: M
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# An incremental sharing read from the delta links the previous snapshot stored

What: the sharing domain of the take starts from the previous complete snapshot's `links` and `linkGrantees` rows and
each drive's stored `deltaLink` (the manifest of task 087), reads only the items the delta reports as changed, refreshes
their permissions and drops the removed ones; a drive without a stored link, or whose delta answers `resyncRequired`,
falls back to the full read; `--full` forces it.
Why: the full sharing read is the take's longest part over the tenant (about 20 minutes of the 24.6-minute live take of
2026-10-31, most of it the permission calls under throttling); a nightly run changes little.
Done when: over the fixtures a second take after one changed item reads that item's permissions only and gives the same
rows as a full take, the manifest says which drives were incremental, and the live take's sharing time drops by an
order of magnitude on an unchanged day.
