---
status: idea
size: S
origin: done/052-group-membership-and-owner-writes.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# Members of a distribution or mail-enabled security group through Exchange

What: `group members add|remove` on a distribution group or a mail-enabled security group runs
`Add-DistributionGroupMember` / `Remove-DistributionGroupMember` through the sidecar (the allow-list and the fake
module grow, with a D-004 note), since Graph refuses the `$ref` write on those group types; the plan reads the
current members through the Admin API's `Get-DistributionGroupMember` (task 042).
Why: task 052 warns and refuses on these groups; the support team's distribution lists are the common case.
Done when: the plan on a distribution group from the fake module shows the diff, `--yes` applies through the
sidecar, and a unified group still goes through Graph.
