---
size: S
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M4 Window
---

# 113 — The window's own names for the snapshot parts in the access card's warnings

## Context

The access card's warnings name the snapshot's domains in the window's words ("SharePoint sites", "OneDrive")
instead of the store's identifiers (`sites`, `oneDrive`), through the wording map of tasks 090 and 104.
Why: a domain identifier is the command line's word; the colleague who reads "sites is partial" on a card has no
command line to look it up in.
Done when: no card renders a bare domain identifier in a warning over the bridge samples, the Vitest dictionary test of
task 104 holds the new entries, and the command line's text is unchanged.
Twelfth round of 2026-11-01, on one branch with tasks 110 and 112; the task session owns the steps below and may reshape
them.

## Docs that move

- none (the wording map is the window's own)

## Not in this change

- The command line's domain names and warnings
- The take's own notes in the manifest

## Steps

1. [ ] The domain names in the wording map (`plan-wording.ts` or its sibling for the access card), every domain of the
       take covered, the access card's warnings reworded through it
2. [ ] The Vitest dictionary test of task 104 forbids a bare domain identifier on a card and holds the new entries;
       100% coverage held; Biome and the typecheck clean
3. [ ] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/112-user-picker-and-window-words` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the Vitest run over the samples>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
