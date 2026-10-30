---
size: S
origin: wip/086-usage-guide-for-the-command-line-and-the-window.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 094 — `scripts/check-fixtures.py` catches two-label domains too

## Context

The `ANY_DOMAIN` pattern of the fixture scan asks for three labels, so `alice@realcompany.com` or `realcompany.hu`
passes the check today; the pattern accepts two labels, the allow-list names the domains the repo may carry
(`contoso.com`, `fabrikam.com`, Microsoft's endpoints, the package registries, the docs' links), and whatever the
tightened scan flags in the fixtures, the docs and the guide is cleaned or allow-listed with a reason.
Why: the repo is public (D-010) and the scan is the gate that keeps tenant data out; a two-label domain is the common
shape of a real tenant's domain.
Done when: a test fixture with a two-label domain fails the scan, the repo passes it with 0 findings, and the
allow-list's entries each carry a one-line reason.
Tenth round of 2026-10-31, on one branch with tasks 088 and 093, done first so the other bundles of the round meet the
tightened scan when they rebase; the task session owns the steps below and may reshape them.

## Docs that move

- `tests/fixtures/README.md`: the scan's rule (two labels, the allow-list and its reasons)
- `CONTRIBUTING.md`: the "Checks before a commit" line if the scan's invocation or its expectations change

## Not in this change

- A scan of the git history or of the GitHub issues (the mirror reads the task files, which the scan covers)
- The anonilm scan of the project-pal plugin: a maintainer's tool, not the repo's gate

## Steps

1. [ ] The pattern: two or more labels, with the allow-list of domains the repo may carry, each with a one-line reason
2. [ ] The scan over the repo: every new finding cleaned (a fixture or doc value rewritten to `contoso.com`) or
       allow-listed with its reason
3. [ ] A self-test of the scan: a two-label domain in a fixture fails, the repo passes with 0 findings
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
