---
size: S
origin: wip/067-publish-the-repository-with-a-rewritten-history.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 068 — The `windows` job on every push to `main`, not only on pull requests and tags

## Context

The `if:` of the `windows` job in `.github/workflows/ci.yml` runs it on a push to `main` too (a docs-only change
still skips it through the `changes` job), and the D-005 sentence in `docs/development.md` follows.
Why: D-005 kept the Windows job off the `main` pushes to spare a private repository's billed minutes; since D-010
the repository is public and its standard runners are free, so the published exes and the window's smoke test
can run on the squash commit itself, not only on the pull request before it.
Done when: a push to `main` shows the `windows` job running and green, and the docs name the new rule.
Part of the coverage and CI bundle (070, 068) of the sixth round of 2026-10-30: both edit `ci.yml`, so they
travel in one branch. Decided 2026-10-30: the widening is taken (the follow-up of task 067 named it as a
separate decision; the maintainer took it with this round's plan).

## Docs that move

- `docs/development.md`, "CI and releases": the `windows` job's trigger sentence and the D-005 matrix sentence
- `.github/workflows/ci.yml`: the header comment and the job's comment

## Not in this change

- Any other change to the CI matrix (the coverage jobs are task 070's)

## Steps

1. [x] The `if:` of the `windows` job: pull requests, tags and pushes to `main`; the comments
2. [x] The docs sentence
3. [ ] Verification: the pull request's run (the job runs as before); the `main` push after the merge shows the job
       running — the main session records it

## Log

- 2026-10-30 — promoted by the main session into the coverage and CI bundle (070, 068)
- 2026-10-30 — `.github/workflows/ci.yml`: the `windows` job's `if:` adds `github.ref == 'refs/heads/main'` beside the
  pull request and the tag (still behind `changes`, so a docs-only push skips it; a manual run of another branch
  skips it as before); the header comment and the job's comment say the new rule and why (D-010: a public
  repository's standard runners are free). `docs/development.md`, "CI and releases": the job's trigger sentence and
  the D-005 sentence (which said the widening was a separate decision) now name task 068; `CONTRIBUTING.md`: the
  sentence that said the Windows job runs on pull requests only.

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
