---
size: S
origin: done/003-graph-client-with-paging-throttling-and-batch.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 036 — A helper that prints a fixture's file name for a URL

## Context

A script or a hidden command (`scripts/fixture-name.py <url>`, or `m365-aegis call … --fixture-name`) that
prints the path a Graph URL maps to under `tests/fixtures/graph`, the short form included when the full name is
over the 120-character limit, so a fixture is authored without running a test to read the name from its
failure; and `scripts/check-fixtures.py` fails on a fixture path over about 200 characters from the repo root,
which git for Windows refuses in a worktree before the commit.
Why: both Graph bundles of 2026-10-29 found the names by running a failing test, and one hit "Filename too long"
at the commit.
Done when: the helper prints the same name the transport computes (a test over the two cases) and the fixture
check refuses an over-long path with the limit in its message.
First of the Graph follow-ups bundle (032–039) of the round of 2026-10-29, because the other seven author
fixtures; the task session owns the steps below and may reshape them.

## Docs that move

- `tests/fixtures/README.md`: how a fixture's name is found
- `docs/development.md`, the fixtures or the Graph client: the helper, one line

## Not in this change

- a recorder that writes fixtures from a live answer

## Steps

1. [x] The helper (the session picks the form), equal to `FixtureGraphTransport.RelativePath` and
       `ShortRelativePath` by a test over the long and the short case
2. [x] The path-length limit in `scripts/check-fixtures.py` with the limit in the message, and a test or a
       documented check of it
3. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — measured before deciding: the longest committed path was 190 characters from the repo root (the
  second members page's `.headers`), and this worktree's root is 68, so 190 is the limit that keeps a worktree
  under git for Windows' 260 (the plan's "about 200" would let a 269-character path through)
- 2026-10-29 — built: the helper is a hidden `--fixture-name` on `call` rather than a script, so the name comes from
  the transport's own code and cannot drift: `FixtureGraphTransport.Describe(Uri)` returns the full name, the
  stored name, the file from the repo root and its length; the command answers locally (backend `local`, no
  sign-in, nothing sent). The transport's short form now also applies when the path under `graph/` is over 150
  characters (`MaxPathLength`), not only a file name over 120: a long object id with a long query, such as a
  group's `transitiveMembers` page 2, made a 198-character path with a 102-character name; the full name is still
  tried first, so every committed fixture is found as before. `scripts/check-fixtures.py` refuses a file over 190
  characters with the limit, the length and the helper in its message

## Deviations from the plan

- The limit is 190 characters, not about 200 (measured, see the Log).
- The short form also covers a path over 150 characters under `graph/`, which the helper alone could not fix.

## Verification

- 2026-10-29 — offline, in the worktree: the Core tests prove the helper's names equal `RelativePath` and
  `ShortRelativePath` for a short URL (a group's owners) and a long one (a message trace window), and that the file
  it names is the fixture the lookups' tests read; a Cli child-process test runs `call GET <trace URL>
  --fixture-name --json` without a fixture folder and gets the same names, backend `local`, no request id; a
  103-character name with a 160-character path is stored in the short form. The fixture check, run by hand with a
  made-up `members@…$skiptoken=page-2&$top=999.try-1.headers` (196 characters) added, exited 1 with "the path is 196
  characters from the repo root, over the limit of 190 …", and 0 again with the file removed (129 files, 0 findings)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
