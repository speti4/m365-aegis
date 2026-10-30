---
status: idea
size: S
origin: done/061-distribution-group-members-through-exchange.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# Owners of a distribution or mail-enabled security group through Exchange

What: `group owners add|remove` on a distribution group or a mail-enabled security group plans against the group's
`ManagedBy` (read through the Admin API's `Get-DistributionGroup`) and applies through the sidecar's
`Set-DistributionGroup -ManagedBy` (the allow-list and the fake module grow, with a D-004 note), the way task 061
routes the member writes; a synced group stays on Graph with its warning; a Microsoft 365 group still goes through
Graph.
Why: task 061 left the owners out by design; the support team's distribution lists change hands as often as members.
Done when: the plan on a distribution group from the fake module shows the owner diff, `--yes` applies through the
sidecar, and a unified group still goes through Graph.
