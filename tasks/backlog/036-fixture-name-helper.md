---
status: idea
size: S
origin: done/003-graph-client-with-paging-throttling-and-batch.md
created: 2026-10-29
milestone: M1 Foundation
---

# A helper that prints a fixture's file name for a URL

What: a script or a hidden command (`scripts/fixture-name.py <url>`, or `m365-aegis call … --fixture-name`) that
prints the path a Graph URL maps to under `tests/fixtures/graph`, the short form included when the full name is
over the 120-character limit, so a fixture is authored without running a test to read the name from its
failure; and `scripts/check-fixtures.py` fails on a fixture path over about 200 characters from the repo root,
which git for Windows refuses in a worktree before the commit.
Why: both Graph bundles of 2026-10-29 found the names by running a failing test, and one hit "Filename too long"
at the commit.
Done when: the helper prints the same name the transport computes (a test over the two cases) and the fixture
check refuses an over-long path with the limit in its message.
