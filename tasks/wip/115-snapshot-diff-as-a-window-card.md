---
size: M
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 115 — The snapshot diff as a window card

## Context

A card that picks two snapshots of the store (the newest and the one before it by default) and shows the diff of
task 098 per domain, the added and the removed rows, over a bridge request on the same Core read; no live call.
Why: the change list is written for the administrator at the command line today; the support colleague who asks "who
got access since yesterday" works in the window.
Done when: over the mock transport the card shows one added and one removed row per domain from two fixture
snapshots, says plainly when fewer than two snapshots exist, a Vitest test holds it over a bridge sample, and the usage
guide has the card.
Twelfth round of 2026-11-01, on one branch with task 114; the task session owns the steps below and may reshape them.
The parallel window bundle (tasks 110, 112, 113) adds a user picker and the domain names to the wording map: this card
registers itself without reshaping the cards' shared files more than the registration needs, and the second of the two
bundles to merge resolves the overlap.

## Docs that move

- `docs/usage.md`: the window's cards section gets the diff card
- `tests/contracts/`: the diff request and its answer, and the "fewer than two snapshots" answer
- `README.md`: the window's feature list, if it names the cards

## Not in this change

- A `--user` filter on the card (the read of task 114 may be wired later)
- Taking a snapshot from the window

## Steps

1. [ ] The bridge request over the diff read (two picks, default newest and the one before), the answer with both
       snapshots' ages and the per-domain rows, the plain answer when fewer than two snapshots exist; the contract
       samples; the Core and Gui tests hold it
2. [ ] The card: the two snapshots, the per-domain added and removed rows, the warnings in the window's words, the plain
       state; the mock transport answers from the sample
3. [ ] Vitest over the bridge sample, 100% coverage held; Biome and the typecheck clean; the GUI `--smoke` passes
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-11-01 — promoted on `task/114-snapshot-diff-user-and-card` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the card over the mock transport, the Vitest run, the smoke run>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
