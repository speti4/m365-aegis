---
status: idea
size: S
origin: wip/076-group-owners-read-through-managedby-for-distribution-groups.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# `group owners-missing --all-types` judges a distribution group by its `ManagedBy`, not by Graph's empty list

What: with `--all-types`, a distribution or mail-enabled security group counts as ownerless only when its `ManagedBy`
is empty, read through the sidecar's `Get-DistributionGroup` over every such group in one call (the route task 076
gave `group owners`); a synced group stays judged by Graph, with the footer; the backend is `graph+pwsh`.
Why: today every distribution group is listed as ownerless because Graph keeps no owners for it, so the list is noise
for a tenant with many distribution groups.
Done when: over the fake module an ownerless distribution group is listed and one with `ManagedBy` is not, a Microsoft
365 group still answers from Graph, and the command stays under a second over the fixtures without `--all-types`.
