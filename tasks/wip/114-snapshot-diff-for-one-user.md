---
size: S
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 114 — `snapshot diff --user <x>`: one person's gained and lost access between two snapshots

## Context

A `--user` filter of `snapshot diff` (task 098) that lists what one user gained and lost between the two
snapshots, the nested memberships (`userGroups`, with the via group) included, in the sections of the access view.
Why: the diff of task 098 compares direct memberships only and lists the whole tenant; "what changed for this person
since yesterday" is the question the access view answers for today and the diff should answer over time.
Done when: over the two fixture snapshots the filtered diff lists the user's added and removed rows per section, a
nested membership gained through a new group shows with its via group, and the command answers under a second from two
tenant-sized files.
Twelfth round of 2026-11-01, on one branch with task 115; the task session owns the steps below and may reshape them.
The live timing over two tenant snapshots is the main session's. The take side (`SnapshotTake.cs`, the manifest) is
the parallel bundle's (tasks 097, 099, 100, 101, not merged yet): this task reads the store as D-011 describes it and
changes no table, column or manifest key.

## Docs that move

- `docs/usage.md`: the "What changed between two snapshots" section gets the `--user` filter
- `README.md`: the Status line's "one person's changes between two snapshots" is reworded at the close by the main session

## Not in this change

- A diff of the whole tenant's nested memberships (the unfiltered diff stays over direct memberships)
- The window card (task 115, same branch)

## Steps

1. [ ] The user resolved in both snapshots (object id, else user principal name, else address), the filter over every
       domain of the diff plus the `userGroups` closure with the via group, in the sections of the access view
2. [ ] The command line's `--user` option with its table and envelope (the output contract's snapshot tests); the fixture
       snapshots of task 098 extended with a nested membership gained through a new group
3. [ ] The live timing over two tenant snapshots (the main session's) in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/114-snapshot-diff-user-and-card` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the fixture rows per section, the via group, the live timing>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
