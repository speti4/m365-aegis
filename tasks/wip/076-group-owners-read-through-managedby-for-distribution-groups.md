---
size: S
origin: done/064-distribution-group-owners-through-exchange.md
created: 2026-10-30
started: 2026-10-30
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

1. [ ] `group owners <group>` routes a distribution or mail-enabled security group to the Exchange owners read of
       task 064 (`ManagedBy` resolved with `Get-EXORecipient`), with the backend named as the members read does
2. [ ] A unified or security group stays on Graph; the "Graph keeps no owners" footer is shown only for the group
       types Graph cannot answer and Exchange is not available for
3. [ ] Cli tests over the fake module: the distribution group's owners, the unified group's Graph answer, the footer
       cases; snapshots updated where the rendering changed
4. [ ] The coverage gate stays at 100% (`scripts/check-coverage.py`)

## Log

- 2026-10-30 — promoted on `task/081-exchange-follow-ups` with task 081

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
