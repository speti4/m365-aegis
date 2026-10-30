---
status: idea
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M5 Snapshot and views
---

# Spike: the snapshot store's format and size, recorded as a decision

What: a measured spike over the fixtures and one live read: what a nightly snapshot of groups, sites, sharing and
mailbox permissions holds for this tenant (620 mailboxes), how large it is as JSON files versus one SQLite file under
the home folder, how the access view of task 084 would query it in under a second, and how a day's snapshot is kept
or pruned; the answer is a decision record (D-011) with the store's shape, not code.
Why: M5 has no task yet and every M5 item depends on the store; a format chosen in the first implementation is a
one-way cost later (D-005 keeps the data under the home folder, never in the repo).
Done when: D-011 is `proposed` with the format, the size per snapshot, the query path and the retention, and tasks 087
and 084 are unblocked by it.
