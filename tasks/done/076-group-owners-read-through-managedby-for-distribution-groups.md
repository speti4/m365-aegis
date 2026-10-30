---
size: S
origin: done/064-distribution-group-owners-through-exchange.md
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 076 — `group owners <group>` reads a distribution group's owners from `ManagedBy`

## Context

The read command `group owners <group>` on a distribution group or a mail-enabled security group lists the
owners from the sidecar's `Get-DistributionGroup` (`ManagedBy`, resolved with `Get-EXORecipient`), the way the owner
writes of task 064 read them; a Microsoft 365 or security group stays on Graph.
Why: since task 064 the write plans against Exchange's owners while the read still shows Graph's empty list with the
"Graph keeps no owners" footer; a support colleague checks the owners before changing them.
Done when: `group owners <distribution group>` lists the `ManagedBy` owners over the fake module with backend
`graph+pwsh`, a unified group still answers from Graph, and the footer is gone for the Exchange-routed types.
Part of the Exchange follow-ups bundle (081, 076) of the seventh round of 2026-10-30; the task session owns the
steps below and may reshape them.

## Docs that move

- `docs/development.md`, the groups domain and the Exchange routing of the distribution group reads (as task 061
  documented the members): the owners read's route and backend
- `README.md`: the `group owners` line, if it names the Graph-only footer

## Not in this change

- `group owners-missing` over the Exchange-routed group types
- The window's group requests

## Steps

1. [x] `group owners <group>` routes a distribution or mail-enabled security group to the Exchange owners read of
       task 064 (`ManagedBy` resolved with `Get-EXORecipient`), with the backend named as the owner writes name it
       (`graph+pwsh`)
2. [x] A unified or security group stays on Graph; the "Graph keeps no owners" footer is shown only for the group
       types Graph cannot answer and Exchange is not available for
3. [x] Cli tests over the fake module: the distribution group's owners, the unified group's Graph answer, the footer
       cases; snapshots updated where the rendering changed
4. [x] The coverage gate stays at 100% (`scripts/check-coverage.py`)

## Log

- 2026-10-30 — promoted on `task/081-exchange-follow-ups` with task 081
- 2026-10-30 — Core: `GroupLookups.OwnersAsync(ExchangeBackends, …)` resolves the group over Graph and, for a group
  `GroupWrites.ThroughExchange` admits (a distribution or mail-enabled security group that is not synced), lists the
  owners with the owner writes' own read (`GroupWrites.ExchangeOwnerListAsync` over `ExchangeOwnersAsync`: the
  sidecar's `Get-DistributionGroup` `ManagedBy`, one piped `Get-EXORecipient`, a role group by its name); every
  other group's owners come from Graph as before. `GroupOwners.FromExchange` is `true` for the Exchange answer and
  left out of the envelope otherwise, so a Graph answer's envelope is unchanged. The Graph-only overload stays.
  No cmdlet joined the allow-list: `Get-DistributionGroup` and `Get-EXORecipient` are on it since task 064.
- 2026-10-30 — Cli: `group owners` runs over `ExchangeRunner` (the layers' overload, as the writes do), so the
  backend is `graph+pwsh` for an Exchange-routed group and `graph` otherwise; the table's footer "Graph keeps no
  owners…" is written only when Graph cannot keep the owners and Exchange did not answer (a synced group). The
  command's description names the Exchange route.
- 2026-10-30 — tests: Cli `LookupCommandTests` over the fixtures and the fake module (Sales: Alice from `ManagedBy`,
  `fromExchange`, backend `graph+pwsh`; Finance Staff's table: Carol and the role group, no footer; Finance: Graph's
  owners, no `fromExchange`, backend `graph`; Sales Synced: the footer stays), `TableViewTests` (no footer with
  `FromExchange`), Core
  `DistributionGroupWritesTests.TheOwnersReadOfADistributionGroupIsItsManagedByAndAMicrosoft365GroupsIsGraphs`. No
  snapshot rendered a `group owners` table, so none changed.
- 2026-10-30 — merged in #98 (squash); the one follow-up (`owners-missing --all-types` over `ManagedBy`) approved
  and filed as task 089; closed on `main` by the main session.

## Deviations from the plan

- The read of `group members` is still Graph's (task 061 routed the member writes, not the read), so the backend is
  named as the owner writes name it, `graph+pwsh`.

## Verification

- 2026-10-30 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`
  (clean), `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings), `dotnet test --no-build
  -c Release --coverlet …` (1269 tests, 0 failed), `scripts/check-coverage.py` (Core 5014/5014 lines, 2768/2768
  branches; command line 1894/1894 lines, 468/468 branches), `scripts/changelog.py --check` (up to date),
  `scripts/check-fixtures.py` (323 files, 0 findings). The live line (`group owners <a cloud-only
  distribution group>` against the tenant, the owners as the Exchange admin center shows them) is the main
  session's.
- 2026-10-31 — live, after the merge: `group owners <a cloud-only distribution group>` answered in 6 s with a cold
  sidecar, backend `graph+pwsh`, `fromExchange: true`, one owner, and the table carried no footer; the maintainer
  confirmed the same single owner in the Exchange admin center. `group owners <a Microsoft 365 group>` answered in
  1 s from Graph, backend `graph`. The tenant that day: 2446 groups, 135 distribution (120 synced), 13 mail-enabled
  security (4 synced), 434 Microsoft 365, 1871 security (1798 synced).

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-30` by the main session after the merge
