---
size: S
origin: done/061-distribution-group-members-through-exchange.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 064 — Owners of a distribution or mail-enabled security group through Exchange

## Context

`group owners add|remove` on a distribution group or a mail-enabled security group plans against the group's
`ManagedBy` (read through the Admin API's `Get-DistributionGroup`) and applies through the sidecar's
`Set-DistributionGroup -ManagedBy` (the allow-list and the fake module grow, with a D-004 note), the way task 061
routes the member writes; a synced group stays on Graph with its warning; a Microsoft 365 group still goes
through Graph.
Why: task 061 left the owners out by design; the support team's distribution lists change hands as often as
members.
Done when: the plan on a distribution group from the fake module shows the owner diff, `--yes` applies through
the sidecar, and a unified group still goes through Graph.
Part of the writes follow-ups bundle (064, 065, 066) of the sixth round of 2026-10-30; the task session owns the
steps below and may reshape them. The D-004 note (the new cmdlet on the allow-list and why) is written by the
main session on `main` at the close from the text the pull request description gives. The live line is a
`group owners add <distribution group> <user> --dry-run` on a cloud-only distribution group from the main
session; nothing is applied.

## Docs that move

- `docs/development.md`, "Writes and plans" and the Exchange layers: the owner writes' route per group type
- `README.md`: the bundle's own heading
- `tasks/decisions/D-004`: a note on `Set-DistributionGroup` (the main session, at the close)

## Not in this change

- Owner writes on a Microsoft 365 group (Graph, task 052) or a synced group (refused with the warning)
- `ManagedBy` of a dynamic distribution group

## Steps

1. [ ] The read: `ManagedBy` through the Admin API's `Get-DistributionGroup` (or the sidecar where the Admin API is
       not there), resolved the way 061 resolves members
2. [ ] The plan and the apply: `GroupWrites` routes an owner write on a distribution or mail-enabled security group
       to the sidecar's `Set-DistributionGroup -ManagedBy`; the allow-list (`ExoCmdlets`), the fake module and the
       Pester tests grow; a synced group keeps the warning; a unified group stays on Graph
3. [ ] Core and Cli tests over the fake module and the recorded answers
4. [ ] Docs; the D-004 note text in the pull request description

## Log

- 2026-10-30 — promoted by the main session into the writes follow-ups bundle (064, 065, 066)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
