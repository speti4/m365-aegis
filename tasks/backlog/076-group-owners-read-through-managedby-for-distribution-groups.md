---
status: idea
size: S
origin: done/064-distribution-group-owners-through-exchange.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# `group owners <group>` reads a distribution group's owners from `ManagedBy`

What: the read command `group owners <group>` on a distribution group or a mail-enabled security group lists the
owners from the sidecar's `Get-DistributionGroup` (`ManagedBy`, resolved with `Get-EXORecipient`), the way the owner
writes of task 064 read them; a Microsoft 365 or security group stays on Graph.
Why: since task 064 the write plans against Exchange's owners while the read still shows Graph's empty list with the
"Graph keeps no owners" footer; a support colleague checks the owners before changing them.
Done when: `group owners <distribution group>` lists the `ManagedBy` owners over the fake module with backend
`graph+pwsh`, a unified group still answers from Graph, and the footer is gone for the Exchange-routed types.
