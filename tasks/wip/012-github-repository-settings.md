---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 012 — GitHub repository settings by hand after the first push

## Context

The workflow of `docs/git-workflow.md` (branch, pull request, squash merge, eight required checks, linear
history) holds only with settings that live in the repository on GitHub, not in a file: the ruleset on `main`,
the ruleset that keeps the `v*` tags, squash as the only merge method with the pull request text as the
message, private vulnerability reporting, Dependabot security updates and secret scanning. The first push and
the first pull request (#3, 2026-10-29) ran every check once, so the check names are known to GitHub. Done when
a pull request shows every check as required, a direct push to `main` is rejected, and the settings as applied
are listed under Verification.

## Docs that move

- `docs/development.md`, "CI and releases": the paragraph on the repository settings adjusted to what was
  actually applied, if it differs.

## Not in this change

- Any file-borne CI change (`.github/`): the workflows and the hook already exist.
- The two open Dependabot pull requests (#1 the .NET SDK pin, #2 TypeScript 7): the maintainer's decision.
- The issue mirror of the task files (M6).

## Steps

1. [ ] Merge settings: squash only, pull request title and body as the message, delete the branch on merge
2. [ ] Ruleset on `main`: pull request with squash, the eight required checks, linear history, no force push, no deletion
3. [ ] Ruleset on `v*` tags: no deletion, no force push, no update
4. [ ] Private vulnerability reporting, Dependabot alerts and security updates, secret scanning where the plan allows it
5. [ ] Verify: a direct push to `main` is rejected, the pull request of this task shows every check as required
6. [ ] Verification written, the settings recorded

## Log

- 2026-10-29 — promoted in the main session; the settings are planned and applied here, without a task branch

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
