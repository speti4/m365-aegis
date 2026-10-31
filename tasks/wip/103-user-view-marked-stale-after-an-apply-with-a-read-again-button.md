---
size: S
origin: wip/091-access-card-refreshes-after-a-write-card-applies.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M4 Window
---

# 103 — The mailbox access card's user view is marked stale after an apply names that user, with a "Read again" button

## Context

When a write card's apply (task 082's `plan-applied`) names the user whose grants the mailbox access card shows,
the card marks the list as possibly stale and offers a "Read again" button instead of reading on its own; the mailbox
view keeps the automatic refresh of task 091.
Why: the reverse lookup over every mailbox takes tens of seconds (task 043), so task 091 left the user view alone; a
colleague who just granted a right still sees an old list with no sign that it is old.
Done when: over the mock transport an apply for the shown user marks the view and the button sends one reverse lookup,
an apply for another user marks nothing, and a Vitest test holds both.
Eleventh round of 2026-10-31, on one branch with tasks 102 and 104; the task session owns the steps below and may
reshape them.

## Docs that move

- `docs/usage.md`: the mailbox access card's paragraph (the stale mark and the button)

## Not in this change

- An automatic refresh of the user view (the cost rules it out, task 091)
- Any change to the Core's reverse lookup

## Steps

1. [ ] The stale mark on the user view when a `plan-applied` names the shown user (`applied-writes.ts`,
       `mailbox-access-card.tsx`), the "Read again" button that sends one reverse lookup, no mark for another user
2. [ ] Vitest holds both cases over the mock transport, 100% coverage held; Biome and the typecheck clean
3. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the two Vitest cases, the coverage line>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
