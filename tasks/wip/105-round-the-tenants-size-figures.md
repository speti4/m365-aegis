---
size: S
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 105 — Round the tenant's size figures to an order of magnitude

## Context

Every exact count of the test tenant (mailboxes, groups, users, sites, teams, forwarders, member edges, owners, shared
items) in `docs/`, `tasks/` and the README becomes a rounded phrase ("about 600 mailboxes", "some 2,500 groups");
measured durations, request counts and percentages stay, so the decisions' reasoning stays readable. The repo is public
(D-010); the audit of 2026-10-31 found no identifier of the tenant in the tree or the history, but its size profile was
readable from the figures in 16 files. Decided 2026-10-31: round, do not delete; the history is not rewritten.
Done when: a grep for the known counts over `docs/`, `tasks/` and `README.md` is empty, `scripts/check-fixtures.py`
passes, and `CHANGELOG.md` is unchanged.

## Docs that move

- `CLAUDE.md`, "Data outside the repo": the tenant's size figures count as tenant data and are written rounded
- `docs/development.md`: the measured sentences keep their timings with rounded counts
- `tasks/decisions/D-004`, `D-011`: the sizing model and the live counts as orders of magnitude
- the done tasks 005, 015, 016, 032, 043, 048, 055, 076, 077, 083, 087, 091, 092 and backlog 100: their Log and
  Verification lines

## Not in this change

- a numeric rule in `scripts/check-fixtures.py` (it would misfire on timings and request counts)
- rewriting the public git history
- the README's rewrite (task 106)

## Steps

1. [ ] Round the figures in `docs/development.md` and `README.md`
2. [ ] Round the figures in `tasks/decisions/D-004` and `D-011`
3. [ ] Round the figures in the done tasks and backlog 100
4. [ ] The CLAUDE.md clause
5. [ ] Verification: the grep is empty, `check-fixtures.py` passes, `CHANGELOG.md` unchanged, `tasks_check` green

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit

## Deviations from the plan

- none yet

## Verification

- pending

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
