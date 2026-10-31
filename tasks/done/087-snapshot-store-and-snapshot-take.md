---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 087 — The snapshot store and `snapshot take`: groups, sites, sharing and mailbox permissions under the home folder

## Context

The store D-011 chose under the home folder (D-005: never in the repo) and the `snapshot take` command that fills it
over the reads the Core already has: groups with members and owners (direct and transitive), sites and their sharing
links, mailbox permissions in both directions; `snapshot list` and `snapshot show` over the snapshots kept, the
retention D-011 set, and a documented scheduled run (Task Scheduler, the silent sign-in of D-003); the sharing-link
read comes from the spike of task 092.
Why: every M5 view reads the store, the access view of task 084 first; a nightly run is what makes the views answer in
a second instead of minutes.
Done when: `snapshot take` writes one snapshot over the fake module and the fixtures in the D-011 shape, a second run
adds a new one and prunes by the retention, the snapshots stay outside the repo, and task 084 reads one.
Tenth round of 2026-10-31, on one branch with task 084; the task session owns the steps below and may reshape them.
D-011 is decided (2026-10-31) and is the contract: one SQLite file per snapshot with integer keys under
`<home>\snapshots\<profile>\`, the `.partial` rename, the manifest, what a snapshot never holds, the retention and the
prune rule, the Task Scheduler run, `Microsoft.Data.Sqlite` with its reason in `Directory.Packages.props`. The sharing
read is built last: the main session runs the 092 spike over the tenant in this round and sends the route it found
(or "no sharing read this round", in which case the `links` and `linkGrantees` tables stay in the schema and the
manifest records the domain as skipped). The live `snapshot take` over the tenant is the main session's, at the end of
the round.

## Docs that move

- `docs/development.md`: a "Snapshot" section (the store, the folder, the schema version, the schedule, the package
  and its reason), the layout
- `docs/usage.md`: the `snapshot take`, `snapshot list` and `snapshot show` entries with the scheduled run (the usage
  guide's tests hold the guide to the help tree)
- `README.md`: the snapshot in the feature list, under its own heading
- `tests/fixtures/README.md`: the fixture snapshot and how it is built, under its own heading
- `Directory.Packages.props`: `Microsoft.Data.Sqlite` with its one-line reason; `licenses/allowed.json` if a license
  is new there
- `tasks/decisions/D-011-snapshot-store-format.md`: a one-line note only if the build departs from the decision (the
  main session writes the sharing route)

## Not in this change

- The access view (task 084, the same branch, the next task)
- The sharing-link read's route and consent (the 092 spike, the main session); `snapshot diff`
- An app-only certificate profile for an unattended schedule (D-011, "Not chosen")

## Steps

1. [x] The package and the schema: `Microsoft.Data.Sqlite` with its reason, the D-011 tables with integer keys and
       their indexes, `pragma user_version` and the `manifest` table, a schema version constant
2. [x] The take: every domain over the Core's existing reads (groups, members, the transitive closure with the via
       group computed at take time, owners with their source, sites, teams, mailboxes, grants both ways, folder
       permissions, forwarding), written to `<name>.sqlite.partial` with the journal off and renamed when complete; a
       failed domain recorded in the manifest, the snapshot stays usable; a leftover `.partial` removed by the next
       take
3. [x] `snapshot list` and `snapshot show` (the envelope and the table, D-006), the retention and the prune after a
       successful take only (never the newest, never a foreign file; the envelope lists what it pruned),
       `keepDaily` and `keepWeekly` per profile in `config.json`
4. [x] The token hygiene test of task 072 extended over the snapshots folder; a fixture snapshot built from the
       fixtures for task 084 and the tests
5. [x] The sharing read, last, over the route the main session sends (or the skipped domain in the manifest)
6. [x] The documented scheduled run (`Register-ScheduledTask`, interactive logon, the silent sign-in, exit code 3 on a
       refused sign-in); docs under *Docs that move*
7. [x] The Verification dated (the fixture snapshot's size, the take's time over the fake module, the prune); `tasks_check`
       without `write` clean apart from the stale index; the live commands for the main session in the report

## Log

- 2026-10-31 — promoted on `task/087-snapshot-store` by the main session
- 2026-10-31 — the package: `Microsoft.Data.Sqlite` 10.0.14 (MIT) with its reason in `Directory.Packages.props`; it
  brings `Microsoft.Data.Sqlite.Core` (MIT) and `SQLitePCLRaw.bundle_e_sqlite3`, `.core`, `.lib.e_sqlite3`,
  `.provider.e_sqlite3` 2.1.13 (Apache-2.0), both licenses already on `licenses/allowed.json`; the lock files restored
  with `--force-evaluate`. `scripts/release.ps1` now fails when `e_sqlite3.dll` is missing from the publish folder.
- 2026-10-31 — the schema (`Core/Snapshots/SnapshotSchema.cs`, `Version` 1, `pragma user_version`): `objects` with
  integer keys and unique indexes on the object id and the user principal name, `members`, `userGroups`, `owners`,
  `sites`, `teams`, `links`, `linkGrantees`, `grants`, `folderPermissions`, `forwarding`, each indexed on its principal,
  and `manifest` (key, value). `SnapshotBuilder` holds the objects (the same object by object id or user principal
  name, else one per kind and name or address), the edges and the closure, and writes the file in one transaction with
  the journal off.
- 2026-10-31 — the take (`SnapshotTake`), thirteen domains over the Core's reads: users and groups (two list reads the
  Core lacked), mailboxes third so their user principal names tie Exchange's ids to Graph's users, members (twenty to a
  `$batch`), the closure with the via group, owners (`$batch` for Graph's, task 076's `ManagedBy` read per group for
  Exchange's), teams, sites with their drive and the group behind them, sharing, OneDrive skipped, the grants (the
  reverse lookup's table, refactored out of `MailboxReads.ReverseAsync` without changing its behavior), the calendars'
  permissions (four mailboxes at a time) and the forwarding. A failed domain is recorded in the manifest, the domains
  that need it fail with it, the snapshot is written (exit 6, no prune); an item that fails makes its domain `partial`;
  a refused sign-in stops the take; a take whose every domain failed writes nothing. `.partial` then rename; the next
  take removes a leftover `.partial` whose name is a snapshot's.
- 2026-10-31 — the sharing read on the route the main session's 092 spike sent: `/sites/{id}/drive?$select=id,owner`
  in `$batch` (`owner.group` is the group behind the site; 404 named as a site without a library, 403 an item that
  failed), each drive's `root/delta` with `Prefer: hierarchicalsharing` (a new `GraphRequest.Prefer`), the marked
  items' `permissions` in `$batch` (a second page followed), links and direct grants into `links`/`linkGrantees`,
  inherited entries and SharePoint's own groups left out, an email-only grantee an `external` object, each drive's
  delta link in the manifest; up to four drives at once.
- 2026-10-31 — `snapshot list` and `snapshot show [<snapshot>]` (local, the table and the envelope), the retention
  and the prune after a take that read every domain (`keepDaily` and `keepWeekly` per profile in `config.json`,
  kept when `login` writes the profile again; a value below 1 is exit 2), never the newest, never a foreign file, a
  refused delete a warning of the take.
- 2026-10-31 — tests: the fixture snapshot built per run by `tests/shared/SnapshotSamples.cs` (never committed) over
  new fixtures (the users, the groups, three groups' members and one's owners, the sites' drives, two drives' delta,
  three items' permissions, six calendars by address); `SnapshotTakeTests` read every table of it, `SnapshotEdgeTests`
  drive a scripted tenant through every failure the reads tell apart, `SnapshotCommandTests` run the commands as child
  processes; the token hygiene test of task 072 runs two takes with the marked tokens and searches the snapshots folder;
  the usage guide's examples run over a home with a profile and the fixture snapshot.
- 2026-10-31 — after the main session's first live take (1,036 permission reads throttled, a 259,348-character note):
  the take's Graph client gets its own budget (8 retries, an hour of waits; a command keeps 4 and a minute) over the
  same transport and request log, the drives' permission batches go one at a time, and a partial domain's note counts
  its failures by code and names three examples; the `access` warnings count and name no site or file.
- 2026-10-31 — merged `origin/main` (#119, #120, #121) into the branch; no conflict, the matrix green on the merged
  tree (1533 tests, the coverage gate at 100%, the tightened `check-fixtures.py` and its `--self-test`).
- 2026-10-31 — docs: a "Snapshots" section and the layout in `docs/development.md`, a "Snapshots" section in
  `docs/usage.md` with the scheduled run (`Register-ScheduledTask`, interactive logon, `-StartWhenAvailable`, exit code
  3 on a refused sign-in), "Snapshots and who can reach what" in `README.md`, "The snapshot fixture" in
  `tests/fixtures/README.md`.
- 2026-10-31 — merged in #122 (squash); closed on `main` by the main session. The follow-ups filed from the pull
  request: the Exchange owners in one bulk call as task 097, the incremental sharing read as task 099, the piped
  calendar names as task 100, the calendar holder matched by address as task 101

## Deviations from the plan

- Two list reads the Core did not have: every user (`GET /users`, `User.ReadBasic.All`) and every group (`GET
  /groups`); D-011's `objects` holds every user and group, and no existing lookup lists them.
- The schema carries a few columns beyond D-011's list: a group's kind and sync flag and a mailbox's type on
  `objects`, the drive on `sites`, and on `links` the kind (`link` or `direct`: a grant to a user or a group is a row
  of its own with its grantee in `linkGrantees`), the link's type and whether it has a password. The manifest keeps
  each domain as JSON under `domain.<name>` with its place under `order.<name>`, and each drive's delta link.
- "Never after a failure": a domain that failed as a whole stops the prune; an item that failed inside a domain (one
  site's library, one mailbox's calendar) makes it `partial` and does not, so a tenant with one unreadable site still
  prunes.
- The 092 route with its own throttling: the permission reads met `activityLimitReached` live, so the take waits
  longer than a command may (8 retries, an hour of waits in all, each `Retry-After` honored) and sends the drives'
  permission batches one at a time while their deltas stay four at once; a take of the tenant takes about 25 minutes.
- The 092 route as sent, with two narrower `$select`s: the drive's `id,owner` (the fixture path of the longer one is
  over the 190 characters `check-fixtures.py` allows, and `quota` and `driveType` are not stored) and the delta's
  `id,name,shared,parentReference` (`size`, `folder`, `file` and `webUrl` are not stored either).

## Verification

- 2026-10-31: `snapshot take` over the recorded answers and the fake module, three cold runs of the built exe: 1.47
  to 1.61 s for the process, 1.05 to 1.08 s of reads (most of it the sidecars' start), exit 0; the fixture snapshot is
  131,072 bytes (128 KB): 17 objects, 14 members, 12 closure rows, 5 owners, 3 sites, 2 teams, 7 links (3 marked items
  of 5 delta items), 6 grantees, 7 grants, 14 calendar entries, 3 forwardings; `sites` partial (the communication
  site's 403), `oneDrive` skipped.
- 2026-10-31: the prune (`SnapshotTakeTests`): seven older snapshots, a foreign `.sqlite`, a leftover `.partial` and a
  foreign `.partial`; the take removed the snapshot `.partial`, pruned three by a 14-day and 4-week retention, kept
  the rest and every foreign file; a take with a failed domain pruned nothing; a take of the same second refused to
  replace the file (exit 8).
- 2026-10-31: the local matrix: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the build (the
  front end included), 1511 tests passed, the coverage gate at 100% of the lines and branches of the Core (5946 lines,
  3147 branches) and the command line (2057, 516), `changelog.py --check`, `check-fixtures.py` (365 files, 0
  findings), `check-vulnerable.py` (0), `nuget-license` over the solution (the SQLite packages MIT and Apache-2.0,
  exit 0); the windows job's steps: `doctor --offline` exit 0, `whoami` exit 3 on an empty home, the window's `--smoke`
  exit 0, `Invoke-Pester tests/release` 7 passed, `release.ps1 -DryRun` signed with a throwaway certificate (publish
  14.6 MB; the SQLite files 2.65 MB of it with ReadyToRun, `e_sqlite3.dll` 1.98 MB).
- 2026-10-31, live (the main session, the real tenant, the broker's cached token; counts only), first run: exit 0 in
  11 min 8 s, 2,920,448 bytes, but `sharing` partial with 1,036 permission reads answered 429 `activityLimitReached`
  inside `$batch` (the command's throttling budget ran out) and a 259,348-character note carried into every `access`
  answer; both fixed (the Log, the deviation).
- 2026-10-31, live, second run of the fixed build: exit 0 in 24 min 35 s (`elapsedMs` 1,474,366), under 7 MB,
  no failed domain, nothing pruned (both snapshots are today's), no `.partial` left, no warning; about a thousand
  users, some 2,500 groups, some 600 mailboxes, members and userGroups under twenty thousand each, a few hundred
  owners and teams, a hundred-odd sites (partial: 2 libraries answered 403, the note bounded), sharing some twenty
  thousand rows with 0 items failed (nothing throttled past the take's budget), oneDrive skipped, a few hundred
  grants, over a thousand folderPermissions, a few dozen forwarding rows. `snapshot list`: 2 snapshots, 0 unreadable.
  The 11 minutes against 24.6 are the price of honoring `Retry-After` on about 1,000 permission reads and of the
  permission batches one at a time: fine for a nightly take.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
