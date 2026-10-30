---
size: S
origin: done/046-window-specific-sign-in-hints.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M4 Window
---

# 057 — The window's own hints for the failures after the sign-in

## Context

The failures that reach the window after a sign-in still carry the command line's hints (Graph's 401 says
`run m365-aegis login`, the sidecar's failure says `run m365-aegis doctor`); the window's hint set of task 046 gets
a line for each of them ("Sign in again", "the Exchange Online tools on this computer need a check; ask the
administrator") and the cards show them.
Why: the same reason as 046 (D-001): a hint that names a command the colleague cannot run is noise.
Done when: every failure kind the bridge can return has a window hint, held by a test over the hint sets, with the
command-line snapshots unchanged.
Part of the Exchange and window bundle (055, 059, 056, 057) of the round of 2026-10-30; the task session owns the
steps below and may reshape them. No live line is needed: the hint sets are held by tests. The parallel writes
bundle does not touch the window, the bridge or the hints.

## Docs that move

- `docs/development.md`, the window's hints paragraph of task 046: the set now covers every failure kind

## Not in this change

- new failure kinds on the bridge
- the front end's wording beyond the hint lines (the cards show what the bridge sends)

## Steps

1. [x] The list of every failure kind the bridge can return (the dispatcher's failures, Graph's, the sidecar's)
2. [x] A window hint for each in the hint set of 046; the cards show them
3. [x] A test that holds the hint set complete over the failure kinds; the command-line snapshots unchanged
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window bundle (055, 059, 056, 057)
- 2026-10-30 — the failure kinds the bridge can return: all thirteen of the contract can reach it (the dispatcher's
  own `usage`; `auth`, `forbidden`, `not-found`, `throttled`, `service-error` from Graph and the Admin API;
  `sidecar-unavailable`, `cmdlet-not-allowed`, `cmdlet-failed` from the sidecar; `partial` without data; `internal`
  from the catch), `refused-without-yes` and `doctor-failed` only once the window gets writes or the doctor, so the
  set covers every kind of `FailureKind`.
- 2026-10-30 — `Auth/SignInHints.cs`: `Failures` (a hint per failure kind; the window's set fills every kind, the
  command line's is empty), `ForSurface(failure)` and `NamesACommand(hint)`. A failure keeps its kind and sentence;
  its hint is replaced by the window's of its kind when it names something only a shell runs (a command in backticks,
  a `--` or `-Scope` flag, an `Install-Module` or `winget` line, an `M365AEGIS_` variable, "run the command") or is
  missing, and stays when it is written for a person (the sign-in set's own, "give the exact display name…", the log
  file of an internal error). Every `error` reply of `BridgeDispatcher` goes through one `Error` helper that applies
  the services' sign-in set, so the window's toasts show the window's hints; the front end shows what the bridge sends
  and is unchanged, but for the mock's two made-up not-found hints, now the window's text.
- 2026-10-30 — tests: `Auth/WindowFailureHintsTests.cs` (32 cases: the set complete over `FailureKind` with no
  command in any hint; 21 failures made by the core's own code, Graph's six, the Admin API's six, seven of the
  sidecar and two internal errors, each reaching the window without a command and unchanged on the command line;
  the predicate; a failure without a hint), and two bridge tests now hold the hint the window shows (a mailbox not
  found, no Exchange session).

- 2026-10-30 — closed by the main session on `main` after #25; no follow-up

## Deviations from the plan

- the rule is a hint that names a command, not every hint: a hint the core wrote for a person (a group lookup's
  "give the exact display name…", the sidecar's role hint, the log file of an internal error) stays, so the window
  loses no useful detail; the `error.json` sample (a group not found) is unchanged

## Verification

- 2026-10-30, local (the worktree): `dotnet test -c Release` 775 passed (the 34 new cases of this task included);
  the command line's snapshots unchanged (no `*.received.*`, `SignInHints.CommandLine.ForSurface` returns the same
  failure); `dotnet format --verify-no-changes` clean; Vitest 57 passed, the front end's lint, typecheck and build
  green. No live line: the hint sets are held by tests.
- 2026-10-30, the bundle's final local matrix (the worktree, after 055's read went back to the sidecar): `dotnet
  restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c Release` (0 warnings), `dotnet test`
  770 passed, `changelog.py --check` up to date, `check-fixtures.py` 275 files 0 findings, `npm ci`, lint, typecheck,
  Vitest 57 passed, build; `Invoke-Pester -Path tests/sidecar` 39 passed; PSScriptAnalyzer (Warning, Error) over the
  sidecar, `tests/sidecar` and `scripts`: no findings; the `windows` job's steps with an empty `M365AEGIS_HOME`:
  `--version` 0, `--json doctor --offline` 0, `--json whoami` 3, `m365-aegis-gui --smoke` 0 (ready after 545 ms,
  top-level window, no sidecar process), `scripts/release.ps1 -DryRun` 0.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
