---
status: idea
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# `snapshot take` reads the Exchange-owned groups' owners in one bulk `Get-DistributionGroup`

What: the owners domain of the take reads `ManagedBy` for every distribution and mail-enabled security group in one
`Get-DistributionGroup -ResultSize Unlimited` call (the read task 089 gave `owners-missing --all-types`) instead of one
call per group through the owner read of task 076.
Why: the live take of 2026-10-31 sent about 150 sidecar calls for the owners domain over the tenant's distribution
groups; one call answers them all.
Done when: the owners domain's `read` label names the bulk cmdlet, the fake module's take gives the same owner rows as
before, and the live take's owners domain is measurably faster (a before/after in the Verification).
