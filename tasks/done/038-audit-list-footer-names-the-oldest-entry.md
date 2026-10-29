---
size: S
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 038 — `audit list` says how far the 1000-entry default reached

## Context

When `audit list` stops at `--limit` (1000 by default) and sets `truncated`, the footer and the envelope
name the oldest entry's time, so the reader knows which part of the window the answer covers and can
continue with `--since`.
Why: the live round of 2026-10-29 answered a busy day with 1000 entries down to 15:27 and the footer did not
say so.
Done when: a truncated answer names the oldest entry's time in the table footer and in the result, with a
fixture test over a two-page answer.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The same footer applies to `signin list` if it shares the window and the limit.

## Docs that move

- `docs/development.md`, the audit lookups: the truncated footer and the `--since` continuation

## Not in this change

- a `--continue` or cursor option
- the `--user` filter (task 037)

## Steps

1. [x] The oldest entry's time in the result record and the envelope when `truncated` is set
2. [x] The table footer names it, with the `--since` hint
3. [x] A two-page fixture, the Core and Cli tests, the snapshot
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference: `activityDateTime` takes `ge` and `le` on the directory audit and
  `createdDateTime` `ge` and `le` on the sign-in log (https://learn.microsoft.com/graph/api/resources/directoryaudit,
  https://learn.microsoft.com/graph/api/resources/signin)
- 2026-10-29 — built: `DirectoryAuditReport` and `SignInReport` gain `oldest`, the time of the oldest entry listed,
  set only when `truncated` is (null and left out of the envelope otherwise). A `--since` alone cannot continue a
  newest-first list that stopped at the limit (it would list the newest again), so both lookups gained
  `--until <date>` (`… le …` in the `$filter`; `--days` counts back from it, as `trace` does; an end in the future or
  before `--since` is a usage error), and the line under a truncated table reads "stopped at the --limit of N: the
  oldest entry listed is from <time> UTC; --since <start> --until <oldest> lists from there back"; the first line
  names the end when there is one. `signin list` shares the window and the limit, so it got the same footer and
  field. The two-page fixture is a made-up window ending 2026-10-29T00:00:00Z

## Deviations from the plan

- `--until` added to `audit list` and `signin list`: the continuation the footer names needs an end, which `--since`
  alone cannot give.

## Verification

- 2026-10-29 — offline: the Core tests stop a two-page directory audit at `--limit 3` on its second page (two request
  ids) and name 2026-10-28T18:44:09Z as the oldest, the same for alice's two sign-in pages, and name no oldest
  under the limit; the child-process test reads `oldest` and `until` from the envelope; the snapshots of the
  truncated audit and sign-in tables show the continuation line, and the accepted changes of the existing
  snapshots are only that line and the `oldest` field
- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token): `audit list --days 1` exit 0, 1000 rows, the footer "stopped at the --limit of 1000: the oldest entry listed is from 2026-10-29 15:17:10 UTC; --since 2026-10-28T21:02:30Z --until 2026-10-29T15:17:10Z lists from there back"; that `--since … --until …` exit 0 in 1.3 s, 1 request, 1000 entries and truncated again (a busy day: the footer chain continues), so Graph takes `activityDateTime le` with `$orderby`; `signin list --days 1 --limit 5` ended with the same footer for sign-ins

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
