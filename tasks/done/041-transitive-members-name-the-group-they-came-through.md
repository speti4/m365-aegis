---
size: S
origin: done/033-group-members-transitive.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-30
milestone: M1 Foundation
---

# 041 — `group members --transitive` names the group a nested member came through

## Context

A `via` column (and field) on the nested rows of `group members --transitive`: the direct member group
through which the member is reached, found by one `members` request per nested group (batched through `$batch`),
behind the same switch or a `--via` option so the plain transitive list stays one round trip.
Why: `transitiveMembers` does not say which nested group carries a member; the support question after "is she in
the group" is "through which one", and the window's group page and the M5 access view ask the same.
Done when: a nested row names its group from fixtures and live, the request count equals the nested groups plus
one, and the direct list stays unchanged.
Part of the Exchange M2 bundle (041–044) of the round of 2026-10-29, because the group expansion of task 044
reuses it; the task session owns the steps below and may reshape them. The live check runs in the main session's
signed-in round.

## Docs that move

- `docs/development.md`, the group lookups: the option, the request count and the row shape
- `README.md`: the option under the bundle's own heading
- `tests/fixtures/README.md`: the nested groups' member fixtures

## Not in this change

- a transitive owners view
- the window's group page

## Steps

1. [x] The option on `group members`, one `members` request per nested group through `$batch`, the `via` field
   and column on the nested rows; the direct list and the plain transitive list unchanged
2. [x] Fixtures for the nested groups' members, the Core and Cli tests, the snapshot
3. [x] Live check (main session): the 14-nested-group security group of task 033, the request count
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange M2 bundle (041–044)
- 2026-10-29 — checked against the reference before coding: JSON batching takes at most 20 requests per `$batch`
  call, each item with its own `headers` (`ConsistencyLevel: eventual` for the advanced query of a members list),
  the items answered in any order and matched by `id`, a throttled item a 429 of its own inside a 200 batch
  (https://learn.microsoft.com/graph/json-batching, https://learn.microsoft.com/graph/throttling#throttling-and-batching);
  the members list as task 033 checked it (`$select` an advanced query, 999 to a page)
- 2026-10-29 — built: `group members --via` (implies `--transitive`): after the direct and the transitive list,
  one `members` request per group of the transitive list through `GraphClient.BatchAsync` (twenty to a call; an
  item with a next link reads the rest outside the batch), then from each direct member group down through those
  lists (a group met twice followed once, so a cycle ends); a nested member's `via` is the direct member group(s)
  it is reached through. `GroupMember.Via` and `GroupMembers.Via` are init properties left out of the envelope
  when not asked for, so the direct and the plain transitive envelopes (and the window's bridge sample) are
  unchanged; the public `MembersAsync(graph, name, transitive, ct)` stays and a new overload takes `via`. The
  table adds a `via` column. `GroupLookups.TransitiveAsync`, `Mark`, `MembersOfAsync` and `ExpandAsync` are the
  internal pieces task 044 reuses. The fixture transport now answers a `$batch` without a recorded answer item by
  item from the items' own fixtures; the client's batch test moved from two recorded batch answers to item
  fixtures. Request count for Finance in the fixtures: 5 (the resolution, the direct list's two pages, the
  transitive list, one `$batch` with one item)

## Deviations from the plan

- `--via` is its own option (implying `--transitive`) rather than the behavior of `--transitive`, so the plain
  transitive list stays one round trip beside the resolution, as the Context allows.
- The request count is "the plain transitive list plus one `$batch` per twenty groups": the batch carries one
  `members` request per group of the transitive list (nested groups at every level, the direct member groups
  among them), which is what following the nesting down needs.

## Verification

- 2026-10-30 — offline, in the worktree, on the bundle's tip after the merge of `main` (9b6214b): the whole local
  matrix green (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and
  without the front end with 0 warnings, `dotnet test` 599 tests, `Invoke-Pester -Path tests/sidecar` 27,
  PSScriptAnalyzer 0 findings, `changelog.py --check`, `check-fixtures.py` 250 files and 0 findings, the commit
  rules' self-test; with an empty `M365AEGIS_HOME` `doctor --offline` exit 0 and `whoami` exit 3; the window's
  `--smoke` exit 0; `release.ps1 -DryRun` green); the new test data through the anonymizer scan, which flagged only
  the made-up contoso and example addresses and number-shaped quotas
- 2026-10-30 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build,
  the broker's cached token): `group members <id> --via` on a synced distribution list of 258 transitive members (2
  direct, 256 through 1 nested group) exit 0 in 1402 ms (1275 ms on a second run), backend `graph`, 4 request ids
  (the resolution, the direct and the transitive list, one `$batch`); all 256 nested rows name their group in `via`
  (one `{id, displayName}` each), no direct row carries `via`, `data.via` is true, and the table shows the `via`
  column with the footer "2 direct, 256 through the 1 nested group(s) among them". `--transitive` alone: exit 0 in
  1117 ms, 3 request ids, 258 members, no row with a `via` property (the envelope unchanged). The 14-nested-group
  security group of task 033 had changed since: its 14 member groups are empty now, so it answered 14 members and 0
  nested, as Graph's own `transitiveMembers` does

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after #22)
- [x] moved to `done/` with `closed: 2026-10-30`
