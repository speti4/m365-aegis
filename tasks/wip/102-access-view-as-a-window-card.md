---
size: M
origin: wip/084-access-view-for-one-user-from-the-snapshot.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 102 — The access view as a window card

## Context

A card of the window that asks for a user (by name or address, the user lookup's picker) and shows what `access
<user>` of task 084 answers from the newest snapshot: the groups with their via groups, the sites, the shared items,
the mailboxes and calendars, the counts of what everyone reaches, the snapshot's age and its warnings; a bridge request
over the same Core read, no live call.
Why: "who can reach what" is the support team's question, and the support colleague works in the window, not the
command line (the M4 and M5 done-whens).
Done when: over the mock transport the card answers for a fixture user with the snapshot's date, says so plainly when no
snapshot exists, a Vitest test holds it over a bridge sample, and the usage guide has the card.
Eleventh round of 2026-10-31, on one branch with tasks 103 and 104; the task session owns the steps below and may
reshape them. The access read of task 084 (`SnapshotAccess`) is used as it is: the snapshot bundles of the round (097,
098, 099, 100, 101) add to the store and the take, none of them changes the access read's shape.

## Docs that move

- `docs/usage.md`: the window's cards section gets the access card
- `tests/contracts/` (the bridge contract samples): the new request and its answer
- `README.md`: the window's feature list, if it names the cards

## Not in this change

- A live (non-snapshot) access read from the window
- The `snapshot take` from the window, or a card that shows the snapshot list
- The diff view (task 098) as a card

## Steps

1. [ ] The bridge request (`BridgeProtocol`, `BridgeDispatcher`) over `SnapshotAccess`, read-only, with the no-snapshot
       answer; the contract sample under `tests/contracts/`; the Core and Gui tests hold it
2. [ ] The card: the user picker of the user lookup, the sections of the access view, the snapshot's age and its
       warnings, the plain "no snapshot" state; the mock transport answers from the sample
3. [ ] Vitest over the bridge sample, 100% coverage held; Biome and the typecheck clean
4. [ ] Docs under *Docs that move*; the Verification dated (the GUI `--smoke` too); `tasks_check` without `write`
       clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session

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
