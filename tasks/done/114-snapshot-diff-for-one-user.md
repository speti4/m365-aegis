---
size: S
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
started: 2026-11-01
closed: 2026-11-01
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

1. [x] The user resolved in both snapshots (object id, else user principal name, else address), the filter over every
       domain of the diff plus the `userGroups` closure with the via group, in the sections of the access view
2. [x] The command line's `--user` option with its table and envelope (the output contract's snapshot tests); the fixture
       snapshots of task 098 extended with a nested membership gained through a new group
3. [x] The live timing over two tenant snapshots (the main session's) in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/114-snapshot-diff-user-and-card` by the main session
- 2026-11-01 — Core: `SnapshotDiff.Compare` takes an optional user. With one, the six domain statements are swapped for
  seven sections of that user, each a statement per snapshot with the user's key as a parameter (`$user_main`,
  `$user_other`) and compared with the same attach and `not in` over keys: `groups` (the `userGroups` closure, keyed on
  the group and the via group), `owners`, `sites` (member through a group, or owner), `sharedItems`, `mailboxes`,
  `calendars` (held by the user or by one of the user's groups, direct or nested, keyed on the holder) and
  `forwarding` (of the user's mailbox, or to the user). Each section rests on the manifest domains it reads
  (`userGroups` for everything that comes through a group). The user is found by name in the second snapshot (the
  access view's lookup), then by object id (an external address by its address) in the first; when the second does
  not know the name, the first is asked and the second looked up by identity. A user one snapshot lacks has every
  row of the other added or removed, with a warning; neither is `not-found` (exit code 4), a blank name `usage`.
  `SnapshotDiffReport` gets `user` and `SnapshotDiffEntry` gets `through` (the via group, or the group holding the
  row), both left out of the envelope when null, so the whole-tenant envelope keeps its shape. The six domain
  statements got a `null as through` column and share the sharing and forwarding fragments with the sections.
  No table, column, schema version or manifest key changed.
- 2026-11-01 — Cli: `snapshot diff <a> [<b>] --user <x>`, edited inside the `diff` subcommand only;
  `SnapshotDiffTableView` shows `section … through` columns and a "for <user>" line when the report has a user
  (`HeadersFor`).
- 2026-11-01 — tests: `SnapshotSamples.SeedChangedAsync` adds the group Audit (`…037`), bob as its member and Audit as
  a member of Finance Staff, and the `userGroups` rows a take would compute (bob in Project Phoenix, Audit, Finance
  Staff through Audit). The whole-tenant diff now has members +3 -1 (`SnapshotDiffTests`, its Verify envelope and
  the Cli table updated). `SnapshotUserDiffTests` (bob's 8 rows, dave's 4, the envelope as a Verify snapshot, a
  self-diff, a renamed user principal name, a user one snapshot lacks both ways, an external address, the errors, a
  section left out when `userGroups` failed), `SnapshotCommandTests` (the envelope and the table over the built exe,
  exit codes 4 and 2, the user table as a Verify snapshot), `TableViewTests` (the through column, the user named by
  address or object id). Docs: the usage guide's "What changed between two snapshots" (an example and a paragraph),
  the development guide's "One user's diff", the fixtures README's changed snapshot.
- 2026-11-01 — after the live check: a domain read in part gave one warning per section that rests on it, so a partial
  `sites` read as two near-duplicate lines per snapshot (the `sites` rows, the `sharedItems` rows). It is now one
  warning per snapshot and domain, at its first place, naming every section ("the sites and sharedItems rows of those
  items may show as removed or added"); the whole-tenant diff, where one domain rests on it, reads as before.
  `origin/main` merged (bundle A, #151) without a conflict.
- 2026-11-01 — merged in #156 (squash); closed on `main` by the main session: the partial-domain warnings were folded to one per snapshot and domain after the live check; no follow-up of its own; the two test flakes the pull request names (the sidecar timeout test under local load, the GUI smoke under full redirection) are not filed (the maintainer's call: both pass alone and in CI)

## Deviations from the plan

- Sections, not the diff's domains: `owners` is a section of its own beside the access view's five, and `forwarding`
  covers both the user's own mailbox and a mailbox forwarding to the user.
- What reaches everyone (an organization or anonymous link, a calendar's `Default` or `Anonymous` entry) is not the
  user's own row and stays in the whole-tenant diff, as the access view counts it apart.
- A site's row carries the group behind it as `through`, as the access view's "member of <group>".

## Verification

- 2026-11-01 — over the fixture snapshot and its changed copy, `snapshot diff yesterday latest --user bob@contoso.com`
  lists 7 added and 1 removed: groups +3 (Audit directly, Finance Staff through Audit, Project Phoenix), sites +1
  (Project Phoenix through its group), sharedItems +2 (`Roadmap.pptx` directly and through Finance Staff), calendars +1
  (carol's calendar through Finance Staff), forwarding -1 (bob's external forwarding); owners and mailboxes unchanged.
  Dave's: owners +1, sites +1 (owner), mailboxes +1 -1 (Reception's FullAccess through Finance Staff gone). A self-diff
  is empty, a renamed user principal name gives the same rows by either name, an unknown user is exit code 4.
- 2026-11-01 — live, by the main session with this branch's build, over the tenant's baseline take of the round and
  the incremental take about an hour later, for the user with the most memberships (on the order of a hundred groups),
  cached token, no sign-in: `snapshot diff <older> latest --user <u> --json` exit 0 in 227 ms as a whole cold process
  after one warm-up (`elapsedMs` 137), all seven sections compared, sharedItems 1 added (plausible: the older
  snapshot's sharing read was partial by one throttled drive), every other section 0/0; 5 warnings, all partial ones,
  of which the per-section near-duplicates are folded since (Log); `--table` exit 0; self-diff `latest latest` 0/0;
  an unknown user exit code 4. Well within the budget of a second.
- 2026-11-01 — the local matrix in the worktree after merging `origin/main` (#151, 443d956 and #152): see task 115's
  Verification, the same runs (1630 tests, the coverage gate at 100%).

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-11-01` by the main session after the merge
