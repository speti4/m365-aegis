---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 070 — 100% line and branch coverage of the Core, the command line, the front end and the sidecar, enforced in CI

## Context

Coverage measured in every test layer and held at 100% lines and branches by a CI threshold: `coverlet.MTP` (in
`Directory.Packages.props`, referenced by no test project yet) for Core and Cli, `@vitest/coverage-v8` for the
front end, Pester `CodeCoverage` for the sidecar; an in-process entry for the command line and a dot-sourceable
function layer for the sidecar, since both run as child processes today and no collector sees them; the tests
that close the gaps (14 source files no test names, `WriteRunner` to `DeviceCodePrompts`; 4 untested front-end
modules).
Why: the maintainer's target of 2026-10-30; a public repo whose writes touch a tenant should show every branch
exercised, and a threshold keeps a new branch from landing untested.
Done when: the three coverage jobs fail CI below 100/100, the reports are run artifacts, and the exclusion list
(the WinForms host and P/Invoke shells that need a display, `[ExcludeFromCodeCoverage]` with a reason each) is
in `docs/development.md`, reviewed by the maintainer.
Part of the coverage and CI bundle (070, 068) of the sixth round of 2026-10-30; the task session owns the steps
below and may reshape them. Decided 2026-10-30: this bundle merges last in the round, after the safety tests
(071–075), the writes follow-ups (064–066) and the window task (063) are on `main`, so the threshold is measured
over the final tree: before its pull request the session merges `origin/main`, moves the new child-process tests
of the safety bundle to the in-process entry and closes the gaps the feature bundles opened. The exclusion list
goes to the maintainer before the pull request.

## Docs that move

- `docs/development.md`: a "Coverage" section (the three collectors, the thresholds, the artifacts, the exclusion
  list with a reason each, how to run them locally); "The front end" names the new dev package and its reason;
  "CI and releases" follows the job changes
- `CONTRIBUTING.md`, "Checks before a commit": the coverage runs
- `tests/M365Aegis.Cli.Tests/M365Aegis.Cli.Tests.csproj`: the header comment follows the in-process entry
- `licenses/allowed.json` only if the new package's license is not on it

## Not in this change

- A coverage badge or a third-party coverage service
- Tests of the WinForms host itself (`WinFormsWebView2Host`, the P/Invoke shells): excluded with a reason
- The `windows` job's own coverage run (the Linux `dotnet` job measures Core and Cli; the Windows-only tests are
  excluded or run there without a threshold, the session decides and documents it)

## Steps

1. [ ] `coverlet.MTP` in both test projects, the lock files; the in-process entry of the command line and the Cli
       tests over it (a child-process test or two kept for the exe contract); the `dotnet` job's threshold and
       artifact
2. [ ] The sidecar's dot-sourceable function layer, Pester `CodeCoverage` with its threshold in the `sidecar` job
3. [ ] `@vitest/coverage-v8` with the threshold in `vitest` config, the `frontend` job's artifact
4. [ ] The gap tests: the 14 Core and Cli source files, the 4 front-end modules; `[ExcludeFromCodeCoverage]` with
       reasons; the exclusion list reported to the main session
5. [ ] 068 in the same branch (its own file)
6. [ ] After the other bundles merged: `git merge origin/main`, the gaps closed again, the full local matrix, the
       pull request

## Log

- 2026-10-30 — promoted by the main session into the coverage and CI bundle (070, 068)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
