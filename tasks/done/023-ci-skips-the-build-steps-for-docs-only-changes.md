---
size: S
origin: session 2026-10-29
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 023 — CI skips the build jobs when only docs, tasks, memory or the changelog changed

## Context

A memory-only pull request on 2026-10-29 (run 40891150208) took 2.5 minutes, all of it the `windows` job (the
Linux jobs finish in 40 s), and the task-only pull requests of the day each paid the same for a build of
unchanged code. A first `changes` job in `ci.yml` diffs the pull request (or the push) against its base and
sets a `code` output; when every changed path is under `docs/`, `tasks/`, `.claude/memory/`, `CHANGELOG.md` or
a root markdown file, the `dotnet`, `deps`, `frontend`, `sidecar` and `windows` jobs are skipped and the run
is green in under a minute, while `lint`, `workflow-lint` and `commit-rules` keep running. Not `paths-ignore`:
a required check whose workflow never starts stays pending and blocks the merge under the ruleset (task 012).
Decided 2026-10-29: the `if:` sits on the jobs, not on every step. GitHub reports a job skipped by its
conditional as a passed check, and five job conditions read better than three dozen step conditions; the
condition is `!cancelled() && needs.changes.outputs.code != 'false'`, so a failed `changes` job leaves the
output empty and every build job runs.
Done when: a pull request that touches only a task file reports every required check green within a minute,
a pull request that touches code still runs the full build, and `docs/development.md` ("CI and releases")
names the rule.

## Docs that move

- `docs/development.md`, "CI and releases": the `changes` job and the docs-only rule (which paths count,
  which jobs skip, which keep running, why not `paths-ignore`)
- `.github/workflows/ci.yml` header comment: the sentence "no path filter skips them" gives way to the rule

## Not in this change

- `paths` or `paths-ignore` on the workflow triggers (a required check that never reports blocks the merge)
- the ruleset's list of required checks (unchanged: a skipped job still satisfies its check)
- `commit-rules.yml`, `advisories.yml` and the `lint` and `workflow-lint` jobs (they run on every change)
- the `windows` job's trigger condition (pull requests and tags only, D-005)
- a third-party paths-filter action (one more pinned dependency for a few lines of git)
- `.github/` markdown (issue and pull request templates) counts as code: the conservative side is the cheap one

## Steps

1. [x] `changes` job in `ci.yml`: the diff against the base (the pull request's merge base, the push's
   `before`; a tag, a manual run or an unknown base is the full run), the `code` output
2. [x] `dotnet`, `deps`, `frontend`, `sidecar`, `windows`: `needs: [changes]` and the job-level skip condition
3. [x] `docs/development.md` ("CI and releases") and the `ci.yml` header comment name the rule
4. [x] Verification: zizmor clean; this pull request runs the full build; a docs-only pull request against the
   task branch skips the five build jobs and is green within a minute

## Log

- 2026-10-29 — promoted from the backlog; planned in the main session, no worktree (a workflow and docs change
  on a `task/023-…` branch, the task-file commits on `main` under the admin bypass)
- 2026-10-29 — the `changes` job and the five conditions in `ci.yml`, the rule in `docs/development.md`
  (commit a4d0895, pull request #11). The step's script run by hand in the clone over six cases: a task-only
  range (81d8aa8..04c212a) gives `code=false` as a pull request and as a push, the TypeScript 7 range
  (da2cb38..43f6cef) `code=true`, and a zero `before`, an unknown `before` and `workflow_dispatch` each fall
  back to `code=true`. `uvx zizmor .github/workflows` (1.31.1): no findings, 12 suppressed.
- 2026-10-29 — the docs-only proof: branch `test/023-docs-only-proof` off the task branch with one line in
  `docs/origin.md`, pull request #12 with the task branch as base, closed without a merge once the run was in,
  the branch deleted.
- 2026-10-29 — closed; no follow-up: the two docs moved with the change, and the `.github/` markdown counting as
  code is a scope line of this task, not open work.

## Deviations from the plan

- none

## Verification

- 2026-10-29 — pull request #11 (`ci.yml` and `docs/development.md` changed): run 40893520387, `changes`
  says `code=true` in 5 s, every build job ran and passed (`dotnet` 34 s, `deps` 36 s, `frontend` 23 s,
  `sidecar` 39 s, `windows` 2 min 37 s), the run took 2 min 48 s; `commit-rules` green in its own run.
- 2026-10-29 — pull request #12 (one line in `docs/origin.md`, base the task branch): run 40893585259,
  `changes` logs `docs  docs/origin.md` and `code=false` in 6 s, `dotnet`, `deps`, `frontend`, `sidecar` and
  `windows` skipped, `lint` 7 s, `workflow-lint` 12 s, `commit-rules` 6 s; the run was green in 16 s. Before
  this change the same kind of pull request (run 40891150208, a memory note) took 2 min 32 s.
- 2026-10-29 — `uvx zizmor .github/workflows` 1.31.1: no findings; `python -I scripts/changelog.py --check`,
  `python -I scripts/check-fixtures.py` (16 files, 0 findings) and `scripts/check-commit.sh --self-test` (38
  cases) pass.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-29`
