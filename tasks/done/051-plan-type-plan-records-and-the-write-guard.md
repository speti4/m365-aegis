---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 051 — The `Plan` type, the plan records and the write guard

## Context

A `Plan` in the Core (a title, the changes as current → desired per object, what is already so, the warnings; the
shape the 018 sketch's made-up plan mimics, so the first task card can consume it later), rendered as a table and
inside the envelope by `M365Aegis.Core.Output`; every write command gets `--dry-run` (prints the plan, exit 0) and
`--yes` (applies); without either the plan is printed and the exit is 7 (D-006); an apply writes a plan record (the
plan, each change's result, the timestamps) under `%LOCALAPPDATA%\m365-aegis\plans\` (`AegisHome`, `M365AEGIS_HOME`
for the tests); a partial apply reports per change and exits with a code the main session assigns; no `--force`.
The `call` guard's ad-hoc `{method,url,body}` plan becomes a `Plan` if it fits.
Why: M3 starts here; every write of 052–054 is a plan first, and the window's cards confirm the same plan.
Done when: a fake write in the tests shows the plan under `--dry-run`, refuses with exit 7 without a flag, applies
under `--yes` with a record file in a temp home, and the Cli snapshots hold the three renderings.
Part of the M3 writes bundle (051, 052, 053, 054) of the round of 2026-10-30; the task session owns the steps
below and may reshape them. Decided 2026-10-30: the live check of this round is `--dry-run` only, the apply is
covered by the fixtures and the fake module.

## Docs that move

- `docs/development.md`: a new "Writes and plans" section (the `Plan`, the flags, the record folder, the exit codes)
- `README.md`: the writes under the bundle's own heading
- `CLAUDE.md`, "Conventions": the write line, only if the contract's wording changes
- `tasks/decisions/D-006-…`: a dated note if the implementation refines the contract

## Not in this change

- the writes themselves (052, 053) and the CSV batch (054)
- a plan or confirmation message on the bridge (the first task card, after the colleague's review of 018)
- a plan record browser or `plan list` command (a follow-up if wanted)

## Steps

1. [x] The `Plan` type and its rendering (table, envelope): `Plan`, `PlanChange`, `PlanUnchanged`, `ChangeResult` in
   a new `Core/Operations/` (the folder the layout in `docs/development.md` already named), the table
   `Cli/Output/PlanTableView.cs`
2. [x] The write guard: `PlanGuard` (`--dry-run` 0, neither 7, `--yes` applies, both 2), `WriteFlags` and
   `WriteRunner` in the Cli; a partial apply is the existing exit code 6, no code added
3. [x] The plan record under the home folder's `plans\`: `PlanStore`, written when the apply starts and again when
   it ends
4. [x] The `call` guard on the `Plan` type (one `send` change), its snapshot updated
5. [x] Core and Cli tests, the snapshots of the three renderings
6. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the M3 writes bundle (051, 052, 053, 054)
- 2026-10-30 — `Core/Operations`: `Plan` (operation, title, changes with action, target, detail, current, desired,
  row and result, unchanged, warnings, mode `dry-run|refused|applying|applied|up-to-date`, record, started,
  finished), `WritePlan` (the plan and one step per change) and `PlanBuilder`, `PlanGuard`, `PlanStore`
  (`plans\yyyyMMddTHHmmss.fffZ-<operation>.json`, `-2` on a collision, a temporary file for the final write),
  `CsvRows` for task 054. The shape keeps the 018 sketch's title, changes, unchanged and warnings.
- 2026-10-30 — the guard's outcomes: every change applied 0; some failed 6 (`partial`, the record in the hint); no
  change applied and every failure of one kind is that failure (one change refused with 403 is 8); the apply goes
  past a refused change and stops at auth (3), sidecar-unavailable (11), cmdlet-not-allowed (12) or internal,
  marking the rest `skipped`; a bug or a cancellation during a step records that change as `failed` with
  `internal`/`cancelled` and goes on to the runner (70). A plan with nothing to change is `up-to-date`, exit 0 under
  any flag, no record.
- 2026-10-30 — `call`: a method other than GET is `GraphCall.Plan` (one `send` change, a warning that call reads no
  current state); `--dry-run` 0, no flag 7, `--yes` through the guard with the answer as the data as before and a
  record `…-call.json`; `--dry-run` on a GET is a usage error.
- 2026-10-30 — tests: `PlanGuardTests` (12: dry run, refusal, up-to-date under the three modes, the apply and its
  record read back, two applies in one millisecond, partial, one failure's own code, the stop at a sign-in failure,
  a bug in a step, the envelope snapshot `PlanGuardTests.ThePartialApplyAsTheEnvelopeWritesIt.verified.json`),
  `GraphCallTests` (the plan of a write, a GET has none, `--yes` through the guard with a record, a 403 under
  `--yes`), the Cli snapshots `WriteCommandTests.TheThreeRenderingsOfAPlan` and `.APlanFromAFileFollowsItsLines`,
  `CallCommandTests.ARefusedWriteShowsThePlanAndTheError` re-accepted on the plan table, and child-process tests for
  the flags and the record (`WriteCommandTests`). The shared `tests/shared/PlanSamples.cs` (a still clock, a temporary
  home, a made-up plan, the CSV folder, the home scrub) is linked into both test projects.
- 2026-10-30 — docs: `docs/development.md` "Writes and plans" (the flags and exit codes table, the `Plan`, the
  records, the code path, the writes, the CSV batch) and a pointer from "The output contract"; `README.md` "Writes
  behind a plan"; a dated D-006 note on the guard as built. `CLAUDE.md` keeps its write line: the contract's wording
  did not change.

- 2026-10-30 — closed by the main session on `main` after #24; the approved follow-ups of the pull request are filed as backlog items 055–062; the task-card follow-up (the bridge's plan and confirmation messages, the first card) stays unfiled until the colleague's review of 018, as decided on 2026-10-30

## Deviations from the plan

- The types live in `Core/Operations/` rather than `Core/Plans/` or `Output`: the layout in `docs/development.md`
  already promised `Operations` for the plans.
- Two outcomes the task did not name, both inside the existing codes: nothing to change is exit 0 under any flag
  (nothing would be sent), and an apply where no change succeeded and every failure is of one kind exits with that
  kind's code rather than 6, whose meaning is "some succeeded".
- `call --yes` keeps the answer as the data (the raw call's contract of task 003) and records the plan beside it,
  instead of answering with the plan.

## Verification

- 2026-10-30 — local, in the worktree: `dotnet test --no-build -c Release` 674 passed, 0 failed (Core 535 and Cli
  139 before the last additions), the plan snapshots accepted by renaming; the three renderings and the record
  checked by hand against the fixtures: `group members add Finance dave@contoso.com bob@contoso.com --dry-run` exit 0
  (1 change, 1 unchanged), without a flag exit 7, `--yes` exit 0 with `plans\20261030T…Z-group-members-add.json`,
  `--dry-run --yes` exit 2.
- 2026-10-30 — live, the main session's one signed-in round (the broker's cached token), ten runs of the
  bundle's `--dry-run` check: every dry run exit 0 with mode `dry-run`, the run without a flag exit 7
  `refused-without-yes` with mode `refused` and the same plan, a malformed CSV exit 2 with backend `local`, 0
  requests and 0 ms in the envelope; no file appeared under `%LOCALAPPDATA%\m365-aegis\plans` (0 before, 0 after),
  so neither a dry run nor a refusal writes a record. The runs of 052, 053 and 054 below.
- 2026-10-30 — the merged tree (this bundle on `main` 7001a79, bundle B), locally: locked restore, `dotnet format`,
  the full build with the front end, `dotnet test` 720 passed, changelog and fixture checks (274 files, 0 findings),
  the commit rules' self-test (38 cases), PSScriptAnalyzer 0 findings, Pester 36 passed (B's export and hidden-module
  cases included), `npm ci`, lint, typecheck, Vitest 57 passed, the Vite build, `--version`, `doctor --offline` 0,
  `whoami` 3 in an empty home, `m365-aegis-gui --smoke` 0, `release.ps1 -DryRun`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
