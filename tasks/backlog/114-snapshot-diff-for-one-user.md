---
status: idea
size: S
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
milestone: M5 Snapshot and views
---

# `snapshot diff --user <x>`: one person's gained and lost access between two snapshots

What: a `--user` filter of `snapshot diff` (task 098) that lists what one user gained and lost between the two
snapshots, the nested memberships (`userGroups`, with the via group) included, in the sections of the access view.
Why: the diff of task 098 compares direct memberships only and lists the whole tenant; "what changed for this person
since yesterday" is the question the access view answers for today and the diff should answer over time.
Done when: over the two fixture snapshots the filtered diff lists the user's added and removed rows per section, a
nested membership gained through a new group shows with its via group, and the command answers under a second from two
tenant-sized files.
