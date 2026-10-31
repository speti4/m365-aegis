---
size: S
origin: wip/090-window-plan-warnings-in-the-windows-own-words.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M4 Window
---

# 104 — The group writes' last-owner warning in the window's words when a group write card arrives

## Context

The warning the group owner writes carry when a change leaves a group without an owner names a command (`group
owners-missing`); the window's wording map of task 090 rewords it at the card to what the colleague can do in the window,
and the Vitest test that forbids a `--` flag on a card also forbids a command name.
Why: a command name on a card is the same dead end as a flag for the colleague who never sees the command line (the M4
done-when); task 090 reworded the flags and left the command names for the card that will show them.
Done when: with a group write card over the mock transport no card renders a `--` flag or a `m365-aegis` command name,
the test holds it over the bridge samples, and the command line's text is unchanged.
Eleventh round of 2026-10-31, on one branch with tasks 102 and 103; the task session owns the steps below and may
reshape them. The window has no group write card yet: the wording and its test are written over the plan warning's
bridge sample, so the card that comes later meets them.

## Docs that move

- none (the wording map is the window's own; `docs/usage.md` names no command on a card)

## Not in this change

- A group write card itself (membership, owners) in the window
- The command line's warning text

## Steps

1. [ ] The last-owner warning's wording in the window's map (`plan-wording.ts`), with the group write plan's warning in
       a bridge contract sample under `tests/contracts/`
2. [ ] The Vitest test over every bridge sample forbids a `--` flag and a `m365-aegis` command name on a card; 100%
       coverage held; Biome and the typecheck clean
3. [ ] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the Vitest run over the samples, the command line's unchanged snapshot tests>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
