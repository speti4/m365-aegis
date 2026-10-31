---
size: M
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M4 Window
---

# 112 — A user picker shared by the User access and the Mailbox access cards

## Context

A picker component that suggests users by name or address as the colleague types (the user lookup's read over
the bridge), used by the User access card (task 102) and the Mailbox access card instead of their plain text fields;
the picked user's address goes to the bridge request.
Why: task 077 left no reusable picker, so both cards ask for an address typed in full; the support colleague knows
names, not user principal names.
Done when: over the mock transport typing three letters lists matching users, picking one fills the card's request, a
Vitest test holds it over a bridge sample, and the usage guide's card paragraphs describe the picker.
Twelfth round of 2026-11-01, on one branch with tasks 110 and 113; the task session owns the steps below and may reshape
them. The maintainer's click-through of the real window is optional and the main session's.

## Docs that move

- `docs/usage.md`: the User access and Mailbox access card paragraphs describe the picker
- `tests/contracts/`: a bridge sample for the picker's lookup request and answer, if the existing user lookup sample
  does not serve

## Not in this change

- A picker for groups or mailboxes
- Any change to the Core's user lookup read beyond what the bridge needs (a prefix search is the read of task 077)

## Steps

1. [ ] The picker component over the user lookup's bridge request: suggestions after three letters, keyboard and mouse
       pick, the picked user's address handed to the card; debounce so one keystroke sends at most one request
2. [ ] The User access card and the Mailbox access card use it in place of their text fields; the plain address still
       works when typed in full
3. [ ] Vitest holds the suggestions, the pick and the request over a bridge sample, 100% coverage held; Biome and the
       typecheck clean; the GUI `--smoke` passes
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-11-01 — promoted on `task/112-user-picker-and-window-words` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the Vitest cases, the coverage line, the smoke run>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
