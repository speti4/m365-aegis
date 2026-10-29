---
size: S
origin: done/004-output-contract-envelope-table-and-exit-codes.md
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 021 — An exit code for an unexpected internal error

## Context

Today an unhandled exception ends `m365-aegis` with the runtime's exit code and a stack trace on stderr, outside
the output contract. The condition gets its own `ExitCode` value (a new number; D-006: existing values never
change meaning), `CommandRunner` (the one place every command runs through) catches it, renders it as an error
envelope or an `error:` line like every other failure, keeps the stack trace out of the envelope (a log line
under the home folder's `logs\` instead; task 025 reuses that logging path), and the snapshot and exit-code tests
cover it.
Why: a script or an agent reading the envelope must never see a bare stack trace, and the exit code has to say
"the tool broke" apart from "the request failed".
Done when: a test that throws from a command gets the new code and a well-formed envelope, and `ExitCode` is
documented with the new value in `docs/development.md`.
Bundled with 025, 024 and 008 on one branch (one pull request for the four, main session's decision of
2026-10-29); the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The output contract": the new exit code in the table, and the `logs\` line of "The
  sign-in and the home folder" gains what an internal error writes there

## Not in this change

- MSAL's own log (task 025, same branch)
- a crash reporter, a log rotation policy or a `--verbose` flag beyond what task 025 adds
- any change to the meaning of an existing exit code

## Steps

1. [x] `ExitCode.Internal = 70` in `src/M365Aegis.Core/Output/ExitCode.cs`, `FailureKind.Internal` with the error
   code `internal`
2. [x] the log under the home folder's `logs\` in the Core (`Diagnostics/LogSink.cs`), shared with task 025
3. [x] `CommandRunner.RunAsync` catches every exception except the contract's own `AegisException` and a
   cancellation, writes the stack trace to the log, renders the `internal` failure; with
   `M365AEGIS_TEST_FAULT=internal-error` the runner throws before the work, for the child-process tests
4. [x] the envelope and table snapshots, the in-process runner tests (pipe, terminal, a log that could not be
   written, a cancellation) and the child-process tests (exit 70, the envelope, the log file and its stack trace)
5. [x] `docs/development.md`: the exit codes as a table with 70, the runner's catch, the `logs\` paragraph

## Log

- 2026-10-29 — promoted by the main session into the command line bundle (021, 025, 024, 008)
- 2026-10-29 — plan: 70, `EX_SOFTWARE` of BSD's sysexits.h ("internal software error"), far from the codes that
  say the request failed and not 1, which System.CommandLine's own exception handler and the .NET host return;
  the message names the exception's type and its message, scrubbed like a log line (a message may name an
  account), the hint names the log file; a cancellation stays System.CommandLine's; a fault-injection variable in
  the runner rather than a hidden test command, so the help shows nothing new
- 2026-10-29 — built: `ExitCode.Internal`, `FailureKind.Internal`, `LogSink` (one file a day,
  `m365-aegis-yyyy-MM-dd.log`, appended to by every process with the time, the process id and a category, a
  multi-line entry indented, nothing created before the first line, a write that fails is a `false`, never an
  exception), `CommandRunner.InternalFailure` and `LogInternalError`; `AuthService` maps a broker failure of the
  sign-out to an `auth` failure, which would otherwise have become an internal error now

## Deviations from the plan

- One new environment variable, `M365AEGIS_TEST_FAULT`, the test hook of the child-process tests; documented in
  `docs/development.md` next to the runner.
- `logout` (and `profile remove`) now answer a broker failure of the sign-out with exit code 3 instead of an
  unhandled exception; found while routing every other exception to the new code.

## Verification

- 2026-10-29 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`,
  `dotnet build -c Release` with and without the front end (0 warnings), `dotnet test -c Release` 166 passed, 0
  failed; `python -I scripts/check-fixtures.py` 38 files, 0 findings; `python -I scripts/changelog.py --check` up
  to date; with an empty `M365AEGIS_HOME`: `--version` 0.1.0, `--json doctor --offline` exit 0, `--json whoami`
  exit 3; `scripts/release.ps1 -DryRun` exit 0
- 2026-10-29 — the internal error: the in-process runner tests (a throw in a pipe is a well-formed envelope with
  `internal`, exit 70 and no stack trace; at a terminal the `error:`/`hint:` lines with the log file; a log that
  could not be written says so; a cancellation is not caught) and the child-process tests with
  `M365AEGIS_TEST_FAULT=internal-error` (exit 70, the envelope, the stack trace in `logs\m365-aegis-<date>.log`; a
  run without an error leaves no `logs\` folder); by hand in a temporary home: `--json profile list` and `--table
  profile list` with the fault set exit 70, the log holds both entries with their stack traces

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
