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

1. [x] The inventory: which reads the Core already has for groups (members, owners, transitive), sites and sharing,
       mailbox permissions in both directions, and what each record holds; the fixture counts and a scale to 620
       mailboxes and this tenant's group count (estimated, the live count is the maintainer's)
2. [x] The size measured: the scaled snapshot as JSON files (one per domain, gzip and plain), one JSON document,
       JSON Lines and one SQLite file (text keys and integer keys), in a throwaway script under the scratchpad; the
       write time and the one-user query time of task 084's question over each shape, in process and in a cold .NET
       process; a stress model beside the estimate
3. [x] The retention and the schedule weighed: snapshots per day kept, a prune rule, the Task Scheduler run with the
       silent sign-in, what a snapshot must never contain (tokens, nothing beyond the reads)
4. [x] D-011 filled: Context (the numbers), Decision (the shape, the folder under the home, the query path, the
       retention, the package if any), Not chosen (the other shapes with their numbers); `cost: medium`
5. [x] The task file's Verification carries the measurement table; `tasks_check` without `write` is clean apart from
       the stale index

## Log

- 2026-10-30 — promoted on `task/083-snapshot-store-spike`; D-011 skeleton created by the main session
- 2026-10-30 — inventory: the Core reads groups (`GroupInfo`), direct and transitive members with the via group
  (`GroupMember`), owners in Graph and `ManagedBy` (task 076), teams, sites, every mailbox, FullAccess, Send As and
  Send on Behalf both ways, forwarding and calendar permissions. Missing for the snapshot: sharing links and direct
  grants on files, and the group behind a site. The fixtures hold 7 users, 9 groups, 6 mailboxes, 7 mailbox grants,
  3 forwarders, 5 calendar entries, 3 sites, 2 teams and no sharing link, too few to scale by ratio; the model takes
  the measured tenant counts already in the docs (621 mailboxes, 2445 groups, 38 forwarders) and estimates the rest
  (D-011, "The counts"): 143,000 rows; a stress model of 616,000 rows (twice the groups, 250 shared items a site)
- 2026-10-30 — measured with a throwaway Python script (standard library) and two throwaway .NET 10 consoles in the
  scratchpad, outside the repo: System.Text.Json for the JSON shapes, Microsoft.Data.Sqlite 10.0.14 for SQLite (its
  restore there gave the licenses from the nuspecs: MIT, and Apache-2.0 for the four SQLitePCLRaw 2.1.13 packages;
  `e_sqlite3.dll` win-x64 is 2.0 MB). Every shape gave the same answer for alice (116 groups, 9 sites, 41 shared
  items, 12 mailbox grants, 2 calendar entries)
- 2026-10-30 — checked against the Graph reference: a drive's `delta` with `Prefer: hierarchicalsharing` finds the
  shared items, and processing permissions correctly needs `Sites.FullControl.All`
  (https://learn.microsoft.com/graph/api/driveitem-delta?view=graph-rest-1.0#scanning-permissions-hierarchies)
- 2026-10-30 — D-011 filled and left `proposed`: one SQLite file per snapshot with integer keys under
  `<home>\snapshots\<profile>\`, the closure stored at take time, a 2-day / 14 daily / 8 weekly retention pruned
  after a successful take, an interactive-logon Task Scheduler run on the silent sign-in, `Microsoft.Data.Sqlite`

## Deviations from the plan

- The brief named "the silent sign-in of task 039" for the scheduled run; task 039 is the non-interactive sign-in
  log lookup (`signin list --kind`). The silent sign-in a scheduled run needs is D-003's order (tasks 002 and 013),
  which D-011 cites instead; task 087's backlog text carries the same pointer and is the main session's to correct
- Step 2 measured more shapes than planned (one JSON document, JSON Lines, SQLite with integer keys) and a stress
  model, and timed the JSON and SQLite reads in a cold .NET process beside Python, since the access view is a .NET
  process whose start counts toward the second
- No live read in this session (the main session's signed-in round); the tenant counts to check are named in D-011

## Verification

- 2026-10-30 — the estimated model (143,000 rows; Python 3.13 for the write and the in-process query, median of 3
  writes and 7 queries; the cold .NET 10 process is the wall time of the whole run, median of 3, warm file cache):

  | shape | size | write | one-user query, in process | one-user query, cold .NET process |
  | --- | --- | --- | --- | --- |
  | JSON, one file per domain | 20.2 MB | 68 ms | 65 ms | 250 ms |
  | JSON, one file per domain, gzip | 3.3 MB | 432 ms | 91 ms | 255 ms |
  | JSON, one document | 20.2 MB | 73 ms | 72 ms | not run |
  | JSON, one document, gzip | 3.3 MB | 438 ms | 100 ms | not run |
  | JSON Lines, one file per table | 20.3 MB | 263 ms | 168 ms | not run |
  | SQLite, text ids as keys | 24.7 MB (5.8 MB gzip) | 306 ms | 4.4 ms (5.1 ms recursive) | 111 ms |
  | SQLite, integer keys | 5.1 MB | 114 ms | 1.9 ms | 110 ms |

- 2026-10-30 — the stress model (616,000 rows):

  | shape | size | write | one-user query, in process | one-user query, cold .NET process |
  | --- | --- | --- | --- | --- |
  | JSON, one file per domain | 112.6 MB | 396 ms | 395 ms | 880 ms |
  | JSON, one file per domain, gzip | 20.2 MB | 2583 ms | 552 ms | 915 ms |
  | JSON, one document | 112.6 MB | 425 ms | 472 ms | not run |
  | JSON, one document, gzip | 20.2 MB | 2658 ms | 688 ms | not run |
  | JSON Lines, one file per table | 113.2 MB | 1286 ms | 993 ms | not run |
  | SQLite, text ids as keys | 118.4 MB | 3199 ms | 50 ms (54 ms recursive) | 124 ms |
  | SQLite, integer keys | 30.5 MB | 650 ms | 20 ms | 119 ms |

- 2026-10-30 — `python -I scripts/check-fixtures.py` and `python -I scripts/changelog.py --check` pass;
  `tasks_check` without `write` reports only the stale index; no tenant data and no home-folder path in the change
  (the synthetic data is made up under `contoso.com` and stayed in the scratchpad)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
