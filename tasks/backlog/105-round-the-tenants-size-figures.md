---
status: ready
size: S
origin: session 2026-10-31
created: 2026-10-31
milestone: M6 Release and team
changelog: housekeeping
---

# Round the tenant's size figures to an order of magnitude

What: every exact count of the test tenant (mailboxes, groups, users, sites, teams, forwarders, member edges, owners,
shared items) in `docs/`, `tasks/` and the README becomes a rounded phrase ("about 600 mailboxes", "some 2,500
groups"); measured durations, request counts and percentages stay. CLAUDE.md's "Data outside the repo" names the
size figures as tenant data.
Why: the repo is public (D-010); the audit of 2026-10-31 found no identifier of the tenant in the tree or the history,
but its size profile is readable from the figures in 16 files, and the maintainer counts that as tenant data.
Done when: a grep for the known counts over `docs/`, `tasks/` and `README.md` is empty, `scripts/check-fixtures.py`
passes, and `CHANGELOG.md` is unchanged.
