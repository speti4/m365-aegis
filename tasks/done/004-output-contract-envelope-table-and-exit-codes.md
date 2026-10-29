---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 004 — Output contract (D-006): the envelope and the table for every command, exit codes, snapshot tests

## Context

D-006 is the contract shells, scripts and agents rely on; it has to exist before the lookups multiply. Every
command renders through one path in the command line: the envelope (`Envelope` in `M365Aegis.Core.Output`) in
a pipe or with `--json`, the table (`TableWriter` in `M365Aegis.Cli.Output`) at a terminal or with `--table`; a
result type per command that both renderings read; the `ExitCode` enum mapped from the core's failures (auth,
not found, throttled, partial); snapshot tests of both renderings with Verify (`Verify.XunitV3`, already in
`Directory.Packages.props`) over scrubbed samples. Done when the scaffold's `doctor` and the M1 lookups all
render through it, a snapshot test guards each rendering, and a wrong exit code fails a test. The scaffold left
`Envelope.cs`, `ExitCode.cs`, `OutputMode.cs`, `TableWriter.cs` and `DoctorCommand.cs` as the starting point.
Works offline against fixtures; no tenant needed.

## Docs that move

- `docs/development.md`, "Layout" and the output section: the one rendering path and the result-type pattern
  a new command follows.
- `CONTRIBUTING.md` if the snapshot files' location and the Verify workflow (accepting a `.received` file) need a
  line under the checks.

## Not in this change

- The lookups themselves (005, 006, 007): they adopt the contract when they land; here only `doctor` renders
  through it.
- Changing the meaning of an existing `ExitCode` value (never changes, D-006).
- The window's rendering of a result (the bridge carries the same result types; 017).

## Steps

1. [x] Core: `FailureKind` and `AegisFailure` (the stable kebab-case code, the hint, the `ExitCode` each kind maps
   to), `AegisException` for the core to throw, `CommandResult<T>` (data, failure; the exit code and the envelope
   follow from it); the envelope serializes enums as kebab-case strings; `DoctorReport.ToResult()`
2. [x] Cli: `ITableView<T>` (the columns, the rows and the footer of one result type), `Renderer` (the one path:
   the envelope on stdout, or the table on stdout and the error line on stderr; returns the exit code),
   `CommandRunner` (the stopwatch, the backend, an `AegisException` into the result, the console); `--json`,
   `--table` and the terminal/pipe default stay in `Program.ResolveMode`
3. [x] `doctor` renders through the path (its table view next to the command); a usage error renders through it
   too, so every answer is an envelope or a table; `m365-aegis --json doctor --offline` unchanged in meaning
4. [x] Tests: scrubbed `DoctorReport` samples under `tests/fixtures/doctor/`; Verify snapshots of the envelope
   (Core tests) and of the table and the renderer (Cli tests, in process through `InternalsVisibleTo`) under
   each project's `Snapshots/`; a theory over every `FailureKind` with its exit code and code; the child-process
   tests check the usage envelope, exit 10 with pwsh off PATH and the error code
5. [x] Docs: `docs/development.md` (the rendering path, the result-type pattern, the snapshot workflow),
   `CONTRIBUTING.md` (one line on accepting a `.received` file), `tests/fixtures/README.md` (the doctor
   samples); `.gitattributes` and `.gitignore` for the snapshot files; `check-fixtures.py` scans the snapshots
6. [x] Verification: `dotnet format --verify-no-changes`, build with 0 warnings, tests green, `check-fixtures.py`,
   `changelog.py --check`

## Log

- 2026-10-29 — promoted by the main session; the work runs in the worktree on this branch
- 2026-10-29 — plan, after reading the scaffold: the failure types live in the Core as one record over a kind
  (`AegisFailure`), not an exception hierarchy, because a partial failure is a result with data, not a throw;
  the exception (`AegisException`) only carries the record out of the core. The meta (elapsed, backend, request
  ids) is the runner's, not the result's, so a result type stays pure data the window can carry too (017). The
  table view of a result type lives in the Cli next to its command; the Core types carry no presentation.
- 2026-10-29 — built: `Output/AegisFailure.cs` (`FailureKind`, `FailureKinds.ToExitCode`/`ToCode`,
  `AegisFailure`, `AegisException`), `Output/CommandResult.cs` and `Backend` in the Core, `ToResult()` on
  `DoctorReport`; `Output/ITableView.cs`, `Renderer.cs`, `CommandRunner.cs` in the Cli, the doctor's table view
  inside `DoctorCommand`, the usage error of `Program` through the same path. The envelope writes enums as
  kebab-case strings (`"status":"pass"`; the scaffold wrote the number).
- 2026-10-29 — Verify.XunitV3 33.3.4 (the pin in `Directory.Packages.props`) turned out to be under the Open
  Source Maintenance Fee EULA, not MIT: its nuspec names `OsmfEula.txt`, and the build fails with SC021 unless a
  sponsorship or a time-bounded exemption (`OpenSource`, `SmallRevenue`; renewed within 12 months) is declared
  in MSBuild. The claim is the maintainer's to make, so the pin moved to 32.0.1, the last MIT release (same
  API), and Dependabot ignores the major; the decision is a follow-up in the pull request.
- 2026-10-29 — follow-ups proposed in the pull request: the Verify 33 decision (sponsor, claim an exemption,
  or stay on 32); an exit code for an unexpected internal error (today an unhandled exception ends the process
  with the runtime's code and a stack trace, outside the contract). The request ids of the meta are the Graph
  client's (003), not a follow-up.

## Deviations from the plan

- Verify pinned to 32.0.1 instead of the 33.3.4 the props carried (the license, see the Log).
- The usage error renders through the path too, not only `doctor`: without it a pipe got bare text and the
  contract's "every answer is an envelope or a table" had a hole the size of every typo.
- The envelope's enums are strings now; the meaning of the doctor envelope is unchanged, its bytes are not.

## Verification

- 2026-10-29 — in the worktree, offline: `dotnet restore --locked-mode` clean after the lock files were
  rewritten once for Verify; `dotnet format --verify-no-changes --no-restore` clean; `dotnet build --no-restore
  -c Release -p:SkipFrontendBuild=true` with 0 warnings; `dotnet test --no-build -c Release` 44 passed, 0
  failed (7 of them Verify snapshots, accepted after reading the received files); `python -I
  scripts/check-fixtures.py` 16 files, 0 findings (the snapshot folders included); `python -I
  scripts/changelog.py --check` up to date; `check-vulnerable.py` 0; `nuget-license` clean; `npm-licenses.py`
  20 packages, 0 outside the allowlist; `npm audit --omit=dev` 0; `uvx zizmor .github` 0 findings. By hand:
  `m365-aegis --json doctor --offline` → `{"ok":true,…,"meta":{…,"backend":"local"}}`, exit 0; `--table` → the
  table, `home: …`, `ok`, exit 0; `m365-aegis frobnicate` in a pipe → `{"ok":false,"error":{"code":"usage",…}}`,
  exit 2; `--table frobnicate` → `error: … (usage)` and `hint: see 'm365-aegis --help'` on stderr, nothing on
  stdout, exit 2; `PATH=` empty → the `pwsh FAIL` row, `error: a required check failed (doctor-failed)`, exit
  10.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
