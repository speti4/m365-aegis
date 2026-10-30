---
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M4 Window
---

# 091 — The mailbox access card refreshes after a write card applies a change to the mailbox it shows

## Context

When a write card's apply finishes (task 082's `plan-applied`) for a mailbox the mailbox access card is showing, the
access card reads the mailbox again and marks the refresh; a failed or partial apply refreshes too, so the card shows
what the service holds now.
Why: after a grant the access card shows the old list until the colleague asks again, which looks like a grant that did
not happen; the browser mock's apply changes nothing, so the stale state is visible there too.
Done when: over the fake module an apply on the shown mailbox is followed by one `mailbox-access` request and the new
grant on the card, an apply on another mailbox sends none, and the mock transport's apply changes its state so the
browser shows the refresh.
Tenth round of 2026-10-31, on one branch with task 090; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The front end": the refresh after an apply and the mock transport's stateful apply
- `docs/usage.md`: the mailbox access card's entry, if it describes what the card shows after a grant

## Not in this change

- A refresh of any other card (the user lookup, the group cards) after an apply
- A change to the bridge protocol: `plan-applied` of task 082 is the signal, as it is

## Steps

1. [ ] The signal: the window's `plan-applied` handling tells the mailbox access card which mailbox an apply touched
       (success, failure or partial alike)
2. [ ] The card: when the mailbox is the one shown, one `mailbox-access` request and a visible refresh mark; another
       mailbox's apply sends none
3. [ ] The mock transport's apply changes its state so the browser (`npm run dev`) shows the refresh; a Vitest test
       holds the one-request and the no-request cases
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
