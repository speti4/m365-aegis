---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-31
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

1. [ ] The package and the schema: `Microsoft.Data.Sqlite` with its reason, the D-011 tables with integer keys and
       their indexes, `pragma user_version` and the `manifest` table, a schema version constant
2. [ ] The take: every domain over the Core's existing reads (groups, members, the transitive closure with the via
       group computed at take time, owners with their source, sites, teams, mailboxes, grants both ways, folder
       permissions, forwarding), written to `<name>.sqlite.partial` with the journal off and renamed when complete; a
       failed domain recorded in the manifest, the snapshot stays usable; a leftover `.partial` removed by the next
       take
3. [ ] `snapshot list` and `snapshot show` (the envelope and the table, D-006), the retention and the prune after a
       successful take only (never the newest, never a foreign file; the envelope lists what it pruned),
       `keepDaily` and `keepWeekly` per profile in `config.json`
4. [ ] The token hygiene test of task 072 extended over the snapshots folder; a fixture snapshot built from the
       fixtures for task 084 and the tests
5. [ ] The sharing read, last, over the route the main session sends (or the skipped domain in the manifest)
6. [ ] The documented scheduled run (`Register-ScheduledTask`, interactive logon, the silent sign-in, exit code 3 on a
       refused sign-in); docs under *Docs that move*
7. [ ] The Verification dated (the fixture snapshot's size, the take's time over the fake module, the prune); `tasks_check`
       without `write` clean apart from the stale index; the live commands for the main session in the report

## Log

- 2026-10-31 — promoted on `task/087-snapshot-store` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
