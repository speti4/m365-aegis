---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 073 — An exit code contract snapshot: the `ExitCode` enum's names and values, frozen

## Context

A Verify snapshot of the `ExitCode` enum as `name = value` lines, one per member, plus a check that the exit code
table in `docs/development.md` lists the same pairs, so a renumbering, a renamed member or an undocumented
addition fails a test instead of a reader.
Why: the output contract (D-006) promises that exit codes never change meaning; a script or an agent branches on
them, and the parallel-session rounds already added codes on separate branches (8, 9, 70).
Done when: the snapshot is accepted, the docs check is green, and changing a value in the enum turns the test red.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. A new exit code in this round is handed out by the main session; the
snapshot then changes with it, which is the point.

## Docs that move

- `docs/development.md`, "The output contract": one sentence naming the snapshot and the docs check (the table
  itself is read by the test, so its shape is now a contract too)

## Not in this change

- Any change to the codes or their meaning

## Steps

1. [x] The snapshot test in the Core tests (`Snapshots/`), one `name = value` line per member, in value order
2. [x] The docs check: the exit code table in `docs/development.md` parsed and compared to the enum, both ways
3. [x] The proof that a changed value turns the test red (a Log line, the change not committed)
4. [x] The docs sentence

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)
- 2026-10-30 — `tests/M365Aegis.Core.Tests/Output/ExitCodeContractTests.cs`: `TheExitCodes` verifies the enum as
  `name = value` lines in value order (`Snapshots/ExitCodeContractTests.TheExitCodes.verified.txt`, 14 lines from
  `Ok = 0` to `Internal = 70`, accepted from the first received file); `TheDocsTableListsEveryExitCodeAndNothingElse`
  reads the table under `## The output contract` of `docs/development.md` (the rows after the
  `| code | error code | meaning |` header, each held to the row shape, a code with two rows refused) and compares its
  codes with the enum's; `TheDocsTableNamesTheErrorCodeOfEachExitCode` compares each row's error code with the
  `FailureKind` that maps to the code (`ToExitCode`, `ToCode`), an empty cell for 0;
  `EveryExitCodeButOkHasOneFailureKind` holds the enum and the failure kinds to each other. The repo root is found by
  walking up from the test's folder to
  `docs/development.md`, as `GraphFixtures` finds `tests/fixtures/graph`.
- 2026-10-30 — red proof (not committed): `Forbidden = 18` in `ExitCode.cs`, the Core tests rebuilt and the class run:
  3 of 4 failed — the snapshot, the docs table (`Expected: […, 7, 9, 10, …] Actual: […, 7, 8, 9, …]`) and the error
  codes (`[8] = "forbidden"` missing from the enum's side); `git checkout` restored the file and the 4 passed again.
- 2026-10-30 — docs: `docs/development.md`, a new section "The safety tests" with the exit code paragraph (the table's
  shape is now part of the contract).

## Deviations from the plan

- The docs paragraph sits in a new section of this bundle, "The safety tests", not in "The output contract": the
  round's rule for parallel bundles keeps each bundle's text under its own heading. The section links back to the
  table.
- The docs check also compares the error code column with `FailureKind.ToCode()`, and a fourth test holds every exit
  code but 0 to exactly one failure kind: the table has the two columns, and a code without a kind could never be
  returned.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (905 tests, 0 failed, 0 skipped on Windows; 808 before the bundle), `python -I
  scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (318 files, 0 findings); `Invoke-Pester -Path
  tests/sidecar` 46 passed; PSScriptAnalyzer over the sidecar, its tests and `scripts` 0 findings; the `windows` job's
  command line steps over an empty `M365AEGIS_HOME`: `m365-aegis.exe --version` 0, `doctor --offline` 0, `whoami` 3, the
  home not created by them.
- 2026-10-30 — `ExitCodeContractTests` 4 green; red with `Forbidden = 18` (3 of 4), green again after the revert.
- 2026-10-30 — after `origin/main` (with #87, the writes follow-ups) was merged into the branch, the same matrix on the
  merged tree, all exit 0: 932 tests, 0 failed, 0 skipped, 19 s; `check-fixtures.py` 320 files, 0 findings; the
  changelog check; `Invoke-Pester -Path tests/sidecar` 51 passed; PSScriptAnalyzer 0 findings; `doctor --offline` 0
  and `whoami` 3 over an empty `M365AEGIS_HOME`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
