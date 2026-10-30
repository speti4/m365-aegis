---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M5 Snapshot and views
---

# 083 — Spike: the snapshot store's format and size, recorded as a decision

## Context

A measured spike over the fixtures and one live read: what a nightly snapshot of groups, sites, sharing and
mailbox permissions holds for this tenant (620 mailboxes), how large it is as JSON files versus one SQLite file under
the home folder, how the access view of task 084 would query it in under a second, and how a day's snapshot is kept
or pruned; the answer is a decision record (D-011) with the store's shape, not code.
Why: M5 has no task yet and every M5 item depends on the store; a format chosen in the first implementation is a
one-way cost later (D-005 keeps the data under the home folder, never in the repo).
Done when: D-011 is `proposed` with the format, the size per snapshot, the query path and the retention, and tasks 087
and 084 are unblocked by it.
Ninth round of 2026-10-30, on its own branch; the task session owns the steps below and may reshape them. The main
session created `tasks/decisions/D-011-snapshot-store-format.md` as a `proposed` skeleton in the promote commit (task
sessions create no ids); the task session fills it and leaves it `proposed`. The one live read (the tenant's counts of
groups, sites, sharing links and mailbox permissions) is the main session's; the spike measures over the fixtures and
scales to 620 mailboxes, and names the live numbers as the maintainer's check in the decision.

## Docs that move

- `tasks/decisions/D-011-snapshot-store-format.md`: filled (Context, Decision, Not chosen), `proposed`
- `docs/development.md`: a short "Snapshot" note that points at D-011, if the decision names a folder or a file under
  the home

## Not in this change

- The store's code and `snapshot take` (task 087), the access view (task 084)
- A new NuGet package (SQLite or other): the spike measures with what the SDK and the repo have, or with a throwaway
  script outside the repo, and the decision names the package a later task would add

## Steps

1. [ ] The inventory: which reads the Core already has for groups (members, owners, transitive), sites and sharing,
       mailbox permissions in both directions, and what each record holds; the fixture counts and a scale to 620
       mailboxes and this tenant's group count (estimated, the live count is the maintainer's)
2. [ ] The size measured: the scaled snapshot as JSON files (one per domain, gzip and plain) and as one SQLite file,
       in a throwaway script under the scratchpad; the write time and the one-user query time of task 084's
       question over each shape
3. [ ] The retention and the schedule weighed: snapshots per day kept, a prune rule, the Task Scheduler run with the
       silent sign-in of task 039, what a snapshot must never contain (tokens, nothing beyond the reads)
4. [ ] D-011 filled: Context (the numbers), Decision (the shape, the folder under the home, the query path, the
       retention, the package if any), Not chosen (the other shapes with their numbers); `cost: medium`
5. [ ] The task file's Verification carries the measurement table; `tasks_check` without `write` is clean apart from
       the stale index

## Log

- 2026-10-30 — promoted on `task/083-snapshot-store-spike`; D-011 skeleton created by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the measurement table>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
