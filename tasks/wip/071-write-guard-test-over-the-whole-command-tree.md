---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 071 — A write-guard test over the whole command tree: without `--yes`, nothing is sent

## Context

One table-driven test in the Cli tests that runs every write command (`group members|owners add|remove`,
`mailbox access grant|revoke`, `mailbox forwarding set|clear`, `call` with a non-GET method, each CSV form and
`--resume`) without `--yes` and with `--dry-run`, and asserts that the fixture transport recorded no request
other than GET and the fake sidecar no cmdlet outside the read list, while the exit code is 7 or 0 and the plan
is printed.
Why: SECURITY.md names a write that happens without a confirmation as a vulnerability (D-006); today each
command's tests cover its own guard, and a new write command could land with none.
Done when: the test enumerates the write commands from the command tree itself, so a new write command joins it
without an edit, and it is green.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. The coverage bundle (070) moves the Cli tests to an in-process entry after
this bundle merges, so a new test here uses the harness of today (`CliTests.RunWith`) and stays small.

## Docs that move

- `docs/development.md`, "Writes and plans" (or "Tests"): one sentence naming the guard test as the contract's check
- `SECURITY.md` only if it names the tests that hold the scope (else none)

## Not in this change

- A new write command or a change to the guard itself
- The writes of the window (the bridge has its own confirmation path)

## Steps

1. [x] The enumeration of the write commands from the built command tree (every command that carries the guard
       flags), the inputs each needs (a group, a mailbox, a CSV file, a record) as a table
2. [x] The run without `--yes` and with `--dry-run` per command; the transport's request log and the fake module's
       write file asserted empty of writes; exit 7 or 0 and the plan in the output
3. [x] The sentence in the docs

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)
- 2026-10-30 — the seam: a child process's fixture transport had no request log the test could read, so
  `FixtureGraphTransport` got `RequestLog` (an init property) and `RequestLogVariable` (`M365AEGIS_FIXTURE_REQUESTS`):
  every request is appended to the named file as one JSON line (`method`, `url`, `body`), under one lock for the
  process (the Graph and the Admin API clients share the file); `GraphConnection.Create` and
  `AdminApiConnection.Fixtures` set it from the environment. Only the fixture transport writes it, so a run against
  the network is unchanged. `Program.Main` now gets the tree from `Program.Build()` (the root command and its
  `--json`, `--table` and `--verbose` options), which the test walks too; nothing else in the command line changed.
- 2026-10-30 — `tests/M365Aegis.Cli.Tests/WriteGuardTests.cs`: `WriteCommands()` walks `Program.Build().Root` and keeps
  every command with both `--dry-run` and `--yes` (9 today: `call`, the four group writes, the two access writes, the
  two forwarding writes); `EveryWriteCommandHasItsInputs` holds the tree and the table of inputs to each other both ways
  (and a `--from-csv` option to a CSV sample); `AWriteWithoutYesSendsNothing` (a theory over the tree) runs each write
  as a child process over every layer at once (`M365AEGIS_GRAPH_FIXTURES`, the fake module, the request log, the fake
  module's write file, a temporary home): its arguments without a flag (exit 7, `refused`, at least one change) and with
  `--dry-run` (exit 0, `dry-run`), its CSV sample both ways (`up-to-date` with exit 0 passes too), and for a command
  with `--resume` a record written through `PlanStore.Begin` from the dry run's plan at `applying` (cut short, so every
  change is left to do) resumed both ways; after each run the log holds only GETs, `$batch` calls whose items are GETs
  and Admin API calls of `Get-` cmdlets, the fake module's file is empty, and the `plans` folder holds no record but the
  test's own. `AWriteWithoutArgumentsSendsNothing` runs each write bare and bare with `--dry-run` (exit 0, 2 or 7,
  nothing sent): the part a new write gets without a table line. `ARawCallOfEveryWriteMethodSendsNothing` runs `call`
  with POST, PATCH, PUT and a lowercase `delete`. 23 tests, about 60 child processes, 28 s on this machine when the
  class runs alone (0.3 s for a Graph write, 0.6 s with the sidecar).
- 2026-10-30 — `tests/fixtures/csv/mailbox-forwarding-clear.csv` (bob, carol): `mailbox forwarding clear` had no CSV
  sample.
- 2026-10-30 — red proof 1 (not committed): `WriteFlags.Resolve` returning `Apply` for no flag: 13 of 23 failed, the
  eight group and mailbox writes on `exit 0, mode applied`, the five calls on exit 70 (`call` sends through a path that
  wants `--yes` itself). Red proof 2 (not committed), a hidden write behind a correct answer: `PlanGuard.RunAsync`
  under `DryRun` applying the plan through a store outside the home and still answering `dry-run`: 13 of 23 failed on
  the sinks alone, the Graph writes with `Assert.Empty() Failure: ["$batch item POST /groups/00000000-…`, the mailbox
  writes with `the fake module recorded a write: {"cmdlet":"Add-MailboxPermission",…}` (and `Remove-MailboxPermission`,
  `Set-Mailbox`). Both reverted with `git checkout`; the class green again.
- 2026-10-30 — the time: the first full run of the bundle took the Cli tests from 15 s to 47 s, the guard's theory
  running its child processes one after another. The runs of one command now go at once in two phases (the arguments
  and the CSV sample both ways, then the resume both ways), each run with its own request log and fake module's file
  in a folder of the test (two processes appending to one file collided: `IOException … cannot access the file`), and
  the sinks are read once a phase is done; the bare runs and the raw calls likewise. The guard and the CSV classes
  together: 10 s. The hidden-write proof repeated on the new shape: 13 of 23 red again, now with the raw calls'
  `POST`, `PATCH`, `PUT` and `DELETE` named by the request log; reverted.
- 2026-10-30 — `origin/main` merged into the branch after the writes follow-ups bundle (064–066, #87): the tree now has a
  tenth write, `plan prune --older-than <days>`, a write of local files (backend `local`). The enumeration found it, and
  `EveryWriteCommandHasItsInputs` failed until its line was added: `--older-than 1` over a home that starts with the
  three sample records (`PlanRecordSamples.Write`, `Seeded` on its inputs), so the plan has changes and the record
  count, which must equal the seeded three plus the test's own, proves a prune without `--yes` removes no file. No CSV
  and no resume for it. The conflicts were the two READMEs' bundle sections, both kept, the merged side's first.
- 2026-10-30 — docs: `docs/development.md` "The safety tests", the write guard paragraph (the enumeration, the runs, the
  request log variable, how a new write joins); `tests/fixtures/README.md`, the bundle's section (the new sample).
  `SECURITY.md` names no tests, so it does not change.

## Deviations from the plan

- "A new write command joins without an edit" holds for the enumeration and for the run without inputs: a write needs
  arguments that name objects of the fixtures, which only its author knows, so a new write found in the tree fails
  `EveryWriteCommandHasItsInputs` until its line is added. The test therefore cannot be skipped by a new write, which
  is the point of Why.
- The fixture transport writes a request log under an environment variable (a seam in the Core,
  `M365AEGIS_FIXTURE_REQUESTS`), and `Program.Build()` was split out of `Main`; both were needed to see a child process's requests and to walk the
  tree in process.
- "No request other than GET" reads a `$batch` call by its items and an Admin API call by its cmdlet: both are POSTs
  that read (the user lookups go twenty to a `$batch`, every Admin API read is a POST).
- The runs use every layer at once (Graph, the Admin API and the fake module) instead of a backend per command, so a
  write that moves to another layer stays covered.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (905 tests, 0 failed, 0 skipped on Windows; 808 before the bundle), `python -I
  scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (318 files, 0 findings); `Invoke-Pester -Path
  tests/sidecar` 46 passed; PSScriptAnalyzer over the sidecar, its tests and `scripts` 0 findings; the `windows` job's
  command line steps over an empty `M365AEGIS_HOME`: `m365-aegis.exe --version` 0, `doctor --offline` 0, `whoami` 3, the
  home not created by them.
- 2026-10-30 — `WriteGuardTests` 23 green (9 write commands from the tree, the bare runs, `call` with four methods); red
  on a guard that applies without a flag (13 of 23) and on a hidden apply behind a correct dry run (13 of 23, named by
  the request log and the fake module's file), green again after each revert.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
