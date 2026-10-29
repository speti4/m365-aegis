---
size: S
origin: session 2026-10-29
created: 2026-10-29
started: 2026-10-29
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

1. [ ] `changes` job in `ci.yml`: the diff against the base (the pull request's merge base, the push's
   `before`; a tag, a manual run or an unknown base is the full run), the `code` output
2. [ ] `dotnet`, `deps`, `frontend`, `sidecar`, `windows`: `needs: [changes]` and the job-level skip condition
3. [ ] `docs/development.md` ("CI and releases") and the `ci.yml` header comment name the rule
4. [ ] Verification: zizmor clean; this pull request runs the full build; a docs-only pull request against the
   task branch skips the five build jobs and is green within a minute

## Log

- 2026-10-29 — promoted from the backlog; planned in the main session, no worktree (a workflow and docs change
  on a `task/023-…` branch, the task-file commits on `main` under the admin bypass)

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
