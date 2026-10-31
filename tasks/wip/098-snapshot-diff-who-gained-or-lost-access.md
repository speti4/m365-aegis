---
size: M
origin: wip/084-access-view-for-one-user-from-the-snapshot.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 098 — `snapshot diff <a> <b>`: who gained or lost access between two snapshots

## Context

A read over two snapshots of the store (D-011: the second file attached to the first) that lists, per domain,
what appeared and what disappeared: memberships, owners, sharing links and their grantees, mailbox grants, calendar
permissions and forwarding; `<a>` and `<b>` are snapshot names or relative picks (`latest`, `yesterday`, a date), the
envelope names both snapshots and their ages.
Why: the change view M5 promised after the access view; the maintainer parked it on 2026-10-30 until the store and the
view existed, both landed in the tenth round.
Done when: over two fixture snapshots the diff lists one added and one removed row in each domain, a diff of a snapshot
with itself is empty, and the command answers under a second from two tenant-sized files.
Eleventh round of 2026-10-31, on one branch with task 096; the task session owns the steps below and may reshape them.
The live timing over two tenant snapshots is the main session's. The take side of the store (`SnapshotTake`, the
manifest) belongs to the parallel bundle of tasks 097, 099, 100 and 101: this task adds a read, it changes no
column, table or manifest field the take writes, and it does not change the access read of task 084.

## Docs that move

- `docs/usage.md`: the `snapshot diff` command, its picks and its table
- `README.md`: the command list, if it names the snapshot commands one by one
- `tasks/decisions/D-011-snapshot-store-format.md`: a note only if the attach-and-compare query shape differs from
  what D-011 describes

## Not in this change

- A diff of the `objects` table itself (new or deleted users, groups, sites) beyond what the domains' rows imply
- A window card for the diff
- Any change to the take or to the manifest (the parallel bundle's)

## Steps

1. [ ] The two picks resolved (a name, `latest`, `yesterday`, a date) to two files of the store, the envelope names
       both snapshots and their ages; the error when a pick finds nothing
2. [ ] The diff read with the second file attached (D-011), per domain the added and the removed rows, keyed on the
       principals so a renamed display name is no change
3. [ ] The `snapshot diff` command of the command line with its table rendering and envelope (the output contract's
       snapshot tests); two fixture snapshots with one added and one removed row per domain; a diff of a snapshot with
       itself is empty
4. [ ] The live timing over two tenant snapshots (the main session's) in the Verification; docs under *Docs that
       move*; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/098-snapshot-diff-and-owners-fallback` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the fixture diff's rows per domain, the self-diff, the live timing over two tenant-sized files>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
