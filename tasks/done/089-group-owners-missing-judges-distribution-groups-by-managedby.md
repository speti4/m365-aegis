---
size: S
origin: wip/076-group-owners-read-through-managedby-for-distribution-groups.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M3 Writes with safety
---

# 089 — `group owners-missing --all-types` judges a distribution group by its `ManagedBy`, not by Graph's empty list

## Context

With `--all-types`, a distribution or mail-enabled security group counts as ownerless only when its `ManagedBy` is
empty, read through the sidecar's `Get-DistributionGroup` over every such group in one call (the route task 076 gave
`group owners`); a synced group stays judged by Graph, with the footer; the backend is `graph+pwsh`.
Why: today every distribution group is listed as ownerless because Graph keeps no owners for it, so the list is noise
for a tenant with many distribution groups.
Done when: over the fake module an ownerless distribution group is listed and one with `ManagedBy` is not, a Microsoft
365 group still answers from Graph, and the command stays under a second over the fixtures without `--all-types`.
Tenth round of 2026-10-31, on its own branch; the task session owns the steps below and may reshape them. The live
check over the tenant (the counts with and without `--all-types`, a distribution group with and without `ManagedBy`,
a synced group's footer) is the main session's, in one signed-in round at the end of the round; the task session names
the exact commands and the expected outcomes in its report.

## Docs that move

- `docs/usage.md`: the `group owners-missing` entry, what `--all-types` judges now
- `docs/development.md`: the groups domain's note on the `ManagedBy` route if it lists the commands that use it
- `tests/fixtures/README.md`: the fake module's new distribution groups, if any are added

## Not in this change

- Owners of a distribution group as a write (`group owners add` for a distribution group stays Graph-only, task 076's
  scope)
- A change to `group owners <group>` (task 076 did it)

## Steps

1. [x] The read: with `--all-types`, one `Get-DistributionGroup` call over the distribution and mail-enabled security
       groups through the sidecar's allow-list; `ManagedBy` empty is ownerless; a synced group keeps Graph's judgment
       and the footer; the backend label is `graph+pwsh`
2. [x] Tests over the fake module: an ownerless distribution group is listed, one with `ManagedBy` is not, a Microsoft
       365 group still answers from Graph; the fixtures carry the cases
3. [x] Without `--all-types` the command is unchanged and stays under a second over the fixtures (a timing in the
       Verification)
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index; the live commands for the main session in the report

## Log

- 2026-10-31 — promoted on `task/089-owners-missing-managedby` by the main session
- 2026-10-31 — Core: a new overload `GroupLookups.OwnerlessAsync(ExchangeBackends, bool, …)` beside the unchanged
  Graph one. It takes Graph's list and, under `--all-types`, judges every group `GroupWrites.ThroughExchange` admits
  (a distribution or mail-enabled security group that is not synced) by its `ManagedBy`: one sidecar call,
  `Get-DistributionGroup -ResultSize Unlimited` with the properties `ExternalDirectoryObjectId` and `ManagedBy` only,
  matched to Graph's list by the object id. A group whose `ManagedBy` names anyone is left out and counted in the new
  `OwnerlessGroups.ManagedInExchange` (null, so left out of the envelope, when Exchange was not asked); a group
  Exchange does not list stays as Graph lists it; a synced group stays Graph's judgment. When Graph lists no group
  Exchange owns, the sidecar is not started and the backend stays `graph`. No cmdlet joined the allow-list
  (`Get-DistributionGroup` is a read on it since task 064).
- 2026-10-31 — Cli: `group owners-missing --all-types` runs over `ExchangeRunner` (backend `graph+pwsh`), the
  default list stays on `GraphRunner`, so without the flag nothing changed. Two lines under the table: the groups
  left out because their `ManagedBy` names an owner, and the synced groups listed as Graph sees them ("Graph keeps no
  owners for them, on-premises Active Directory manages them", the wording of `group owners`' footer). The flag's
  help text names the `ManagedBy` judgment.
- 2026-10-31 — fixtures: the ownerless queries' groups had ids the writes bundles gave to other groups (All Staff
  `…035` = Finance Staff, HR Synced `…036` = Sales), which a join by object id would confuse; they are `…03b`, `…03c`,
  `…03d` now. The fake module knows All Staff (empty `ManagedBy`) and Sales Managers (`ManagedBy` alice), and its
  `Get-DistributionGroup` answers every group without `-Identity`. Tests: Core
  `DistributionGroupWritesTests` (the fake module: All Staff listed, Sales Managers left out, Project Phoenix and HR
  Synced from Graph, backend `graph+pwsh`; without `--all-types` no sidecar, backend `graph`), `GroupLookupsTests`
  (a canned sidecar: a synced list with `ManagedBy` stays, a group Exchange does not list stays, the one command
  sent; no sidecar when Graph lists no Exchange group), Cli `LookupCommandTests` (the envelope and the table over the
  built exe), `TableViewTests` (the two footer lines), Pester (the bulk read through the sidecar script).
- 2026-10-31 — merged in #119 (squash); closed on `main` by the main session. Of the pull request's follow-ups the
  Graph fallback without an Exchange role is filed as task 096; the duplicated `tests/fixtures/README.md` is not
  filed: task 094 removed the second copy in #121 the same day

## Deviations from the plan

- The fixtures' ownerless groups got new ids (see the Log); the default list's rendering and counts did not change.
- When Graph lists no distribution or mail-enabled security group, `--all-types` does not start the sidecar and the
  backend is `graph`, not `graph+pwsh`.

## Verification

- 2026-10-31 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`
  (clean), `dotnet build --no-restore -c Release` with the front end (0 warnings), `dotnet test --no-build -c Release
  --coverlet …` (1451 tests, 0 failed), `scripts/check-coverage.py` (Core 5226/5226 lines, 2875/2875 branches;
  command line 1911/1911 lines, 472/472 branches), `scripts/changelog.py --check` (up to date),
  `scripts/check-fixtures.py` (331 files, 0 findings), `tests/sidecar/run-with-coverage.ps1` (122 tests passed, the
  script's 266/266 commands), PSScriptAnalyzer over the sidecar folders (0 findings), the `windows` job's steps
  (`doctor --offline` exit 0, `whoami` exit 3 on an empty home, the window's `--smoke` ready in 719 ms,
  `scripts/release.ps1 -DryRun` with a throwaway certificate, `Invoke-Pester tests/release` 7 passed).
- 2026-10-31 — timing over the fixtures, the built exe, five warm runs: `group owners-missing` 139 to 150 ms (Graph
  alone, the same two groups and "3 … left out" as before); `group owners-missing --all-types` over the fake module
  677 to 704 ms with the sidecar's start: 4 groups (All Staff listed, Sales Managers left out), the two lines under
  the table, backend `graph+pwsh`. The live line over the tenant is the main session's.
- 2026-10-31 — live, by the main session from this branch's Release build (cached token, warm process):
  - `group owners-missing --json`: backend `graph`, a couple of hundred groups, `leftOut` some 1,900, no `managedInExchange`, 1.8 s.
  - `--all-types --json`: backend `graph+pwsh`, most of the tenant's groups (the Graph list plus the left-out ones less the Exchange-managed), `managedInExchange` 17, 6.86 s,
    0 warnings. By kind: a hundred-odd synced distribution groups, a few synced mail-enabled security groups, a hundred-odd Microsoft 365
    groups, a few dozen cloud security groups, the rest synced security groups. The table ended with "N group(s) without
    an owner", the line for the 17 groups left out by `ManagedBy`, and "N synced group(s) as Graph lists them".
  - Every cloud distribution and mail-enabled security group in the tenant has a `ManagedBy` (15 cloud distribution
    groups of 135), so the case of a cloud distribution group that stays listed is covered by the fake module only.
  - Two of the left-out cloud distribution groups, checked with `group owners <id>`: 1 owner each, `fromExchange`
    true, backend `graph+pwsh`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
