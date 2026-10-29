---
size: S
origin: done/005-group-teams-and-sharepoint-lookups.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 032 — `group owners-missing` under a second

## Context

The live round of 2026-10-29 measured `group owners-missing` at 7.9 s over 178 ownerless groups in three
paged requests (`owners/$count eq 0` as an advanced query); the done-when of task 005 asked for well under a
second plus the network. Try a leaner `$select`, the largest page (`$top=999`), a count-only probe first, or
`$batch` over the pages, and measure again live with the request count and the elapsed time in the Log.
Why: the daily "which groups have no owner" question is the one the admin center answers slowest, and the
command line's promise is the speed.
Done when: the live run answers in well under a second plus the network on the same tenant, or the Log states
the measured floor of the endpoint and the hint names it.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live measurement runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, the group lookups: the request shape and the measured floor, if one exists

## Not in this change

- a cached or snapshot answer (M5)
- the other group commands' request shapes

## Steps

1. [x] The request shape offline: `$select`, `$top=999`, a count probe, or `$batch` over the pages, decided
       against the Graph reference and the fixtures
2. [x] The fixture tests follow the new shape; the request count is asserted
3. [x] Live measurement (main session): request count and elapsed time in the Log, before and after
4. [x] The hint names the endpoint's floor if the second is not reached (in `docs/development.md`, see the
       deviations)

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference before deciding: list groups (page size 100 by default, 999 at most;
  the documented kind filters `groupTypes/any(c:c eq 'Unified')` and `mailEnabled eq false and securityEnabled eq
  true and NOT(groupTypes/any(…))` as advanced queries; https://learn.microsoft.com/graph/api/group-list), advanced
  queries (`owners/$count eq 0`, `$count` as a URL segment, `ConsistencyLevel: eventual` with `$count=true`, no
  `$expand` with them; https://learn.microsoft.com/graph/aad-advanced-queries), the query parameters (`$expand` on a
  directory relationship returns at most 20 items and no next link, `@odata.count` on the first page only;
  https://learn.microsoft.com/graph/query-parameters, https://learn.microsoft.com/graph/known-issues#query-parameters)
- 2026-10-29 — decided: the `$select` already held the six fields the kind needs and `$top` was already 999, so
  neither can make it faster; `$batch` over the pages is impossible (each page's skip token comes from the one
  before), and `$expand=owners` would page every group at most 999 at a time without the server-side filter. The
  live answer of task 005 had three pages for 178 listed groups at 999 a page, so the pages were filled by the kinds
  the command leaves out (distribution, mail-enabled security, synced). The default request is now three requests
  side by side: the ownerless Microsoft 365 groups, the ownerless security groups without mail (the two kinds
  Graph keeps owners for, each its documented filter plus `owners/$count eq 0`) and `GET /groups/$count?$filter=
  owners/$count eq 0` for the left-out count; synced groups are still picked out client-side. A 400 or 406 on any
  of them falls back to the wide query, which `--all-types` keeps using. The transport now shares one token task
  between requests sent side by side. `call` gained `--advanced` (`ConsistencyLevel: eventual` on the request and
  every page) so the shapes can be measured raw in the live round
- 2026-10-29 — live round, main session (cached token): the new default exited 9 on both runs (2.8 s cold, 4.8 s warm, 4 request ids): `GET /groups/$count` answered 415, "The supported type(s) 'text/plain' do not match any of the acceptable MIME types 'application/json'", and the fallback caught only 400 and 406. Fixed: `GraphRequest.Accept`, which the client sends as the `Accept` header and the HTTP transport honors instead of JSON; the count request asks for `text/plain` and reads the number; `call` does the same for a path ending in `/$count` (live, `call GET '/groups/$count?…' --advanced` had the same 415, and without `--advanced` Graph answered 400 asking for `ConsistencyLevel: eventual`); the fallback now covers 415 too, with a scripted test. The shapes that answered: `--all-types` (the wide query) exit 0 in 1.8 s, 3 requests, 2117 groups of the tenant's 2445; the raw wide query `call … --advanced --all` 1.35 s, 3 requests, 2117 items (`@odata.count` 2117); the raw Microsoft 365 query 0.66 s, 1 request, 128 items. Task 005's 178 groups were the 2117 less the kinds Graph keeps no owners for, which filled the wide query's three pages; the floor of the default is one page of a narrowed query, about 0.65 s
- 2026-10-29 — re-measured live after the fix: 1.25 s warm and 2.1 s cold (task 005: 7.9 s), 4 request ids, 178
  groups and 1939 left out (178 + 1939 = the 2117 the wide query lists). The fourth request id is a second page of
  the security groups query: the Microsoft 365 query answered in one page live, the count is one answer, and the
  fallback would have added the wide query's three pages; so Graph ended the first page of the 50 ownerless
  security groups early (178 less the 128 Microsoft 365 groups). The requests run side by side, so the floor is the slowest of them, about 0.65 s a
  page; the rest of the wall clock is the process start and the token

## Deviations from the plan

- Not a leaner `$select`, a count-only probe first or `$batch` over the pages (see the Log): the kinds the command
  leaves out are filtered on the server instead, with a `$count` request beside them.
- `call --advanced` added for the live measurement.
- "Well under a second plus the network" is not reached on the wall clock (1.25 s warm): the measured floor of the
  endpoint (about 0.65 s a page of the narrowed advanced query) is in the Log and in `docs/development.md`, not in
  a line under the table, which would print the same sentence on every run.
- The live round found the raw `$count` answering 415 to a JSON-only request; fixed in this change
  (`GraphRequest.Accept`).

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on `03e4d31` (`dotnet restore --locked-mode`,
  `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet
  test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3
  with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the fixture tests assert the three requests
  side by side, `Accept: text/plain` on the count only, and the wide-query fallback on a 400 and on a 415
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release
  build, a cached token), a tenant of 2445 groups: on `0801c30` exit 9 twice (the 415 of the count, see the Log);
  on `03e4d31` `--json group owners-missing` exit 0, 2105 ms cold and 1252 ms warm (`meta.elapsedMs`; wall clock
  2.2 s and 1.3 s), 4 request ids, 178 groups, `leftOut` 1939; the footer "178 group(s) without an owner" and
  "1939 distribution, mail-enabled security or synced group(s) left out …"; `--all-types` exit 0 in 1.8 s, 3
  request ids, 2117 groups

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
