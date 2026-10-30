---
size: S
origin: wip/076-group-owners-read-through-managedby-for-distribution-groups.md
created: 2026-10-30
started: 2026-10-31
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

1. [ ] The read: with `--all-types`, one `Get-DistributionGroup` call over the distribution and mail-enabled security
       groups through the sidecar's allow-list; `ManagedBy` empty is ownerless; a synced group keeps Graph's judgment
       and the footer; the backend label is `graph+pwsh`
2. [ ] Tests over the fake module: an ownerless distribution group is listed, one with `ManagedBy` is not, a Microsoft
       365 group still answers from Graph; the fixtures carry the cases
3. [ ] Without `--all-types` the command is unchanged and stays under a second over the fixtures (a timing in the
       Verification)
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index; the live commands for the main session in the report

## Log

- 2026-10-31 — promoted on `task/089-owners-missing-managedby` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
