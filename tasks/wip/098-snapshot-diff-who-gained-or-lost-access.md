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

1. [x] The two picks resolved (a name, `latest`, `yesterday`, a date) to two files of the store, the envelope names
       both snapshots and their ages; the error when a pick finds nothing
2. [x] The diff read with the second file attached (D-011), per domain the added and the removed rows, keyed on the
       principals so a renamed display name is no change
3. [x] The `snapshot diff` command of the command line with its table rendering and envelope (the output contract's
       snapshot tests); two fixture snapshots with one added and one removed row per domain; a diff of a snapshot with
       itself is empty
4. [ ] The live timing over two tenant snapshots (the main session's) in the Verification; docs under *Docs that
       move*; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/098-snapshot-diff-and-owners-fallback` by the main session
- 2026-10-31 — Core: `Snapshots/SnapshotDiff.cs`, its records beside it (`SnapshotDiffReport`, `SnapshotDiffSide`,
  `SnapshotDiffDomain`, `SnapshotDiffEntry`, `SnapshotChange`), so `SnapshotResults.cs`, which the parallel bundle
  changes, is untouched. `Resolve` turns a pick into a file: `latest` the newest readable snapshot, `yesterday` or a
  date (`yyyy-MM-dd`) the newest readable one taken on that UTC day or before it (the tenant as it stood at the day's
  end, so a gap in the retention still answers), else a snapshot's name with or without `.sqlite`; a word that is none
  is a usage error (exit code 2) with the forms in the hint, a pick that finds nothing exit code 4. `Compare` opens the
  first file read-only and attaches the second (`attach database $file as other`, read-only with the connection), then
  one statement per domain (`members`, `owners`, `sharing`, `grants`, `folderPermissions`, `forwarding`): each side's
  rows as a key of the principals' identities (the builder's: object id, else user principal name, else kind and
  address or name) and the rows whose key the other side lacks. Neither a file's integer keys nor a display name take
  part; a changed right or calendar permission is a removed and an added row; a link with no grantee (organization,
  anonymous) is one row whose `who` says who it reaches. The envelope names both snapshots with their pick and age,
  per domain the counts, the rows (what, who, how, and the object ids where there are any) and the warnings: a domain
  that a manifest records as failed or skipped, or lacks, is left out; a domain that rests on a partial one (`sharing`
  on `sites`) is compared with a warning; a first snapshot newer than the second is read as asked, with a warning.
  `SnapshotStore.Files` became `internal` (one word); no table, column, schema version or manifest key changed, and the
  access read of task 084 is untouched.
- 2026-10-31 — Cli: `snapshot diff <a> [<b>]` (backend `local`, `<b>` defaults to `latest`) in its own method of
  `SnapshotCommand.cs` and its `SnapshotDiffTableView` (domain, change, what, who, how; the two snapshots with their
  ages, the counts per compared domain or "no change in …", the warnings under it).
- 2026-10-31 — tests: `SnapshotSamples.SeedChangedAsync` copies the fixture snapshot as `2026-11-01T060000Z.sqlite`
  and changes it with SQL (one row removed and one added per domain, the group Finance renamed, every integer key moved
  by 100); `SnapshotDiffTests` (the twelve rows and their ids, the envelope as a Verify snapshot, the self-diff by
  `latest`, by name and by date, the picks with a damaged file on the day, the reversed order, a failed, a skipped and
  a missing domain, the errors, a warm diff under a second), `SnapshotCommandTests` (the envelope and the table over
  the built exe, the self-diff's "no change", exit codes 4 and 2, the table as a Verify snapshot, a cold process under
  three seconds), `TableViewTests` (a domain left out). Docs: the usage guide's "What changed between two snapshots",
  the development guide's "The diff" paragraph, the fixtures README's changed snapshot, the README's snapshot bullet.

## Deviations from the plan

- `<b>` is optional and defaults to `latest`, so `snapshot diff yesterday` compares to the newest.
- A date or `yesterday` picks the newest snapshot of that UTC day or before it rather than one of that day only.
- The memberships are the direct ones (`members`); the transitive closure (`userGroups`) is not compared, since every
  change of it follows from a direct one the diff lists.
- The usage guide's examples pick by date and name, not `yesterday`: the guide's test runs them over the one fixture
  snapshot of 2026-10-31, which `yesterday` finds only from the day after.

## Verification

- 2026-10-31 — over the fixture snapshot and its changed copy, `snapshot diff yesterday latest` lists one added and one
  removed row in each of the six domains (12 rows, the renamed group and the moved keys no change), with the sites'
  partial warning for each snapshot; `latest latest`, a name against itself and a date against the same file are
  empty. In the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore` (clean),
  `dotnet build -c Release` with the front end (0 warnings), `dotnet test --no-build -c Release --coverlet …` (1553
  tests, 0 failed), `scripts/check-coverage.py` (Core 6153/6153 lines, 3205/3205 branches; command line 2128/2128
  lines, 528/528 branches), `scripts/changelog.py --check` (up to date), `scripts/check-fixtures.py --self-test` (17
  cases, 0 misses) and the scan (503 files, 0 findings), `m365-aegis.exe doctor --offline` exit 0, `whoami` exit 3 on
  an empty home, the window's `--smoke` ready in 583 ms, `scripts/release.ps1 -DryRun` with a throwaway certificate
  exit 0.
- The live timing over two tenant snapshots: the main session's, pending.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (D-011 needs no note: the comparison attaches the second
      file as it describes)
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
