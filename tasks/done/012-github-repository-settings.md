---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
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

1. [x] Merge settings: squash only, pull request title and body as the message, delete the branch on merge
2. [x] Ruleset on `main`: pull request with squash, the eight required checks, linear history, no force push, no deletion
3. [x] Ruleset on `v*` tags: no deletion, no force push, no update
4. [x] Private vulnerability reporting, Dependabot alerts and security updates, secret scanning where the plan allows it
5. [x] Verify: a direct push to `main` is rejected, the pull request of this task shows every check as required
6. [x] Verification written, the settings recorded

## Log

- 2026-10-29 — promoted in the main session; the settings are planned and applied here, without a task branch
- 2026-10-29 — the promote commit pushed to `main` as the last direct push; then through the REST API: the
  repository patched (`allow_merge_commit=false`, `allow_rebase_merge=false`, `squash_merge_commit_title=PR_TITLE`,
  `squash_merge_commit_message=PR_BODY`, `delete_branch_on_merge=true`, projects and wiki off), the branch
  ruleset 26084811 on `~DEFAULT_BRANCH` (deletion, non_fast_forward, required_linear_history, pull_request with
  squash as the only merge method and 0 required approvals, required_status_checks for the eight jobs, not
  strict, no bypass actors), the tag ruleset 26084812 on `refs/tags/v*` (deletion, non_fast_forward, update),
  `vulnerability-alerts` and `automated-security-fixes` on
- 2026-10-29 — `private-vulnerability-reporting` answers 404 and `security_and_analysis.secret_scanning` 422
  ("not available for this repository"): both are features of a public repository (or of the paid add-on),
  recorded in `docs/development.md` as turned on when the repo goes public; the close of this task goes
  through a pull request because the ruleset already refuses a direct push
- 2026-10-29 — one follow-up, approved by the maintainer: 019 (private vulnerability reporting and secret
  scanning once the repo is public, M6); the two open Dependabot pull requests are the maintainer's decision,
  not a task

## Deviations from the plan

- The strict status-check policy is off: a strict policy re-runs CI on every open branch after each merge to
  `main`, which the minutes budget (D-005) does not afford; squash keeps the history linear anyway.
- Private vulnerability reporting and secret scanning could not be turned on (a private repository on this
  plan); they move to the publishing step of M6.

## Verification

- 2026-10-29 — `gh api repos/speti4/m365-aegis/rulesets` lists 26084811 (branch, active) and 26084812 (tag,
  active); `gh api repos/speti4/m365-aegis/rules/branches/main` lists deletion, non_fast_forward,
  required_linear_history, pull_request (squash, 0 approvals) and required_status_checks (`dotnet`, `deps`,
  `frontend`, `sidecar`, `lint`, `workflow-lint`, `windows`, `commit-rules`; strict=false); an empty commit
  pushed to `main` from a throwaway branch was rejected with `GH013: Repository rule violations found for
  refs/heads/main`; `gh repo view --json …` shows squash as the only merge method, delete-branch-on-merge on,
  projects and wiki off; `vulnerability-alerts` answers 204 and `automated-security-fixes` `enabled: true`; the
  pull request that closes this task lists all eight checks as required

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-29`
