---
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M4 Window
---

# 090 — The window's plan warnings in the window's own words, not the command line's flags

## Context

A plan warning shown on a card is worded for the window: the forwarding card's "(--keep-copy keeps one)" becomes a
sentence about the "Keep a copy" box, and every other warning the `Plan` carries is checked for a flag name; the
command line's wording stays as it is, the window maps or rewords it at the card.
Why: the support colleague never sees the command line (the M4 done-when), and a flag name on a card is a dead end.
Done when: no card of the window renders a `--` flag over the fake module and the mock transport, a Vitest test holds
it over the bridge samples, and the command line's plan text is unchanged (its snapshots untouched).
Tenth round of 2026-10-31, on one branch with task 091; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The front end": where a plan warning is reworded for a card, if the mapping gets a place of
  its own
- `docs/usage.md`: a card's text if the guide quotes a warning

## Not in this change

- The command line's plan wording and its snapshots
- A change to the bridge protocol or its contract samples (the window rewords what it receives)

## Steps

1. [ ] The inventory: every warning a `Plan` can carry (the Core's plan builders) and which ones name a flag
2. [ ] The mapping at the card: the forwarding warning becomes a sentence about the "Keep a copy" box; every other
       flag-naming warning gets its window wording; an unknown warning passes through unchanged
3. [ ] A Vitest test over the bridge contract samples: no card renders a `--` flag; the CLI snapshots are untouched
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/090-window-plan-wording` by the main session

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
