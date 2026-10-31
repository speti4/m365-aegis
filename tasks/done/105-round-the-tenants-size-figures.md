---
size: S
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
closed: 2026-10-31
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

1. [x] Round the figures in `docs/development.md` and `README.md`
2. [x] Round the figures in `tasks/decisions/D-004` and `D-011`
3. [x] Round the figures in the done tasks and backlog 100 (and the drive-level counts of the 092 spike and task 084, found by the sweep)
4. [x] The CLAUDE.md clause
5. [x] Verification: the grep is empty, `check-fixtures.py` passes, `CHANGELOG.md` unchanged, `tasks_check` green

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit
- 2026-10-31 — 23 files: a scripted pass of exact-substring replacements (each asserted to match once), then three sweeps for
  counts next to the tenant's nouns caught the drive-level figures of the 092 spike, the snapshot row counts of 084 and 087
  and the group kinds of 089, which the first grep had missed; the per-kind breakdowns became proportions
- 2026-10-31 — merged as #137 (squash `ca57dc4`); closed on `main` by the main session; no follow-up found

## Deviations from the plan

- the sweep reached beyond the 16 files the audit named: 22 files carried a figure once the drive, item and grantee counts
  of the sharing spike counted too

## Verification

- 2026-10-31 — `git grep` for every known count over `docs/`, `tasks/`, `README.md` and `CLAUDE.md` is empty;
  `scripts/check-fixtures.py`: 498 files, 0 findings; `scripts/changelog.py --check`: up to date; `tasks_check` green

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log (none found)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-31`
