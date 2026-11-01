---
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
closed: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 097 — `snapshot take` reads the Exchange-owned groups' owners in one bulk `Get-DistributionGroup`

## Context

The owners domain of the take reads `ManagedBy` for every distribution and mail-enabled security group in one
`Get-DistributionGroup -ResultSize Unlimited` call (the read task 089 gave `owners-missing --all-types`) instead of one
call per group through the owner read of task 076.
Why: the live take of 2026-10-31 sent about 150 sidecar calls for the owners domain over the tenant's distribution
groups; one call answers them all.
Done when: the owners domain's `read` label names the bulk cmdlet, the fake module's take gives the same owner rows as
before, and the live take's owners domain is measurably faster (a before/after in the Verification).
Eleventh round of 2026-10-31, on one branch with tasks 099, 100 and 101 (the take's Exchange domains and the sharing
read); the task session owns the steps below and may reshape them. The live before/after is the main session's.

## Docs that move

- `docs/usage.md` or `docs/development.md`: the take's owners domain line, if either names the per-group read

## Not in this change

- The `group owners` command's own per-group read (task 076), which stays as it is
- The owners of Graph-owned groups (the `$batch` read stays)

## Steps

1. [x] The owners domain in `SnapshotTake.OwnersAsync`: one bulk `ManagedBy` read over the Exchange-owned groups
       (the read of task 089), the rows matched to the take's groups, the `read` label names the bulk cmdlet
2. [x] The fake module and the take fixtures give the same owner rows as before; the Core tests hold it
3. [x] The live before/after of the owners domain (the main session's numbers) in the Verification
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session
- 2026-10-31 — measure first: every domain of the manifest now carries `elapsedMs`, `graphRequests`, `throttledMs`,
  `adminApiCalls` and `sidecarCalls` (counted by `GraphClient.RequestsSent` and per layer by `ExchangeBackends`), shown
  as the `time` and `requests` columns of `snapshot take` and `snapshot show`; no schema change (manifest keys only).
  Over the fixtures the owners domain sent 4 sidecar calls for 2 Exchange-owned groups (2 per group:
  `Get-DistributionGroup -Identity`, then a piped `Get-EXORecipient`)
- 2026-10-31 — `GroupWrites.ExchangeOwnerListsAsync`: one `Get-DistributionGroup -ResultSize Unlimited`
  (`ExternalDirectoryObjectId`, `ManagedBy`) and one piped `Get-EXORecipient` over every distinct owner name; each
  group's owners are the recipients its names resolve to, a name no recipient answers to is listed as it is (the
  per-group read's rule, now shared through `Unresolved`). The take matches by the group's object id; a group Exchange
  does not list is a `not-found` item, a failed bulk read makes each Exchange-owned group an item that failed. Over the
  fixtures: 2 sidecar calls, the same 5 owner rows (`SnapshotTakeTests`); `SnapshotEdgeTests` hold the missing group,
  the empty `ManagedBy`, the unresolved role group and the failed bulk read
- 2026-11-01 — merged in #151 (squash); closed on `main` by the main session: no follow-up of its own

## Deviations from the plan

- The per-domain timings and request counts (not in the steps) came first, as the round's measurement for 097 and 100

## Verification

- 2026-11-01 — live, the main session, cached token (tenant counts rounded): the owners domain of the baseline take
  (build `5a30bc7`, per-group read) took 42,477 ms with 122 Graph requests and some 34 sidecar calls (about 17
  Exchange-owned groups, 2 calls each); the after-take (build `46118d7`, `--full`) took 31,185 ms with 122 Graph
  requests and 2 sidecar calls, the same order of rows (some 500). The rest of the domain's time is the Graph
  `$batch` read of the other groups' owners, which this task leaves as it is
- 2026-11-01 — local, merged tree: restore `--locked-mode`, format, build, 1586 tests, Core and Cli coverage 100% lines
  and branches, changelog `--check`, check-fixtures `--self-test` and scan, sidecar Pester 124 with 100% of the
  script, `gui/web` lint, typecheck, `test:coverage` and build, `doctor --offline` exit 0, `whoami` exit 3 on an
  empty home, the window's `--smoke` exit 0, `release.ps1 -DryRun` exit 0

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposed them and filed the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-11-01` by the main session after the merge
