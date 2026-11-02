---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 121 — Focus management and status announcements in the window, with card headings, radio groups and the stepper's semantics

## Context

The lookup field stays enabled while a lookup runs (`readOnly`, `aria-busy`, a double-submit guard) so focus never
falls to the body; a write card focuses the new step's heading or first control on a step change and the mailbox
field after Done; each card keeps one persistent `role="status"` text naming the transition ("Looking up Finance…",
"Finance: 4 direct members", "Plan: 2 changes", "Done: every change was made"), refusals stay alerts. Card titles
become `h2` (the cards regions); either/or choices use base-ui `RadioGroup` (one Tab stop, arrow keys); the
stepper's `aria-current` sits on the item with a spoken state; the picker's option name separates name and address
and an empty list is not exposed as expanded.
Why: the UX/UI review of 2026-11-02 (findings A-1, A-3, U-7; A-5, A-6, A-11, A-12): every lookup and write step drops
the focus, nothing is announced to a screen reader, and the page goes from `h1` straight to `h3`.
Decided 2026-11-02: bundled with task 120 on one branch (the same files).
Done when: a keyboard-only lookup and write flow keeps the focus on a named control at every step, axe reports no
`heading-order` hit, and the tests assert the status texts and the focus targets.

## Docs that move

- `docs/development.md`, "The front end": one line on the status region and the focus rule, if the section
  describes the cards' parts (else none)
- the front-end tests for the status texts and the focus targets

## Not in this change

- The forced-colors and focus-ring contrast rules (task 123)
- The vocabulary of the announced texts beyond what is new here (task 122)
- The sticky top bar (task 124)

## Steps

1. [ ] Lookup cards: `readOnly` + `aria-busy` instead of `disabled` on the field while busy, a guard against a second
       submit; the focus stays in the field after the reply
2. [ ] `mailbox-write-cards.tsx` / `plan-view.tsx`: on a step change, focus the step's heading or first control
       (`tabIndex={-1}` targets, a `useEffect` on `state.step`); the mailbox field after Done and "Change the picks"
3. [ ] One persistent `sr-only` `role="status"` per card, its text set on every transition; refusals keep
       `role="alert"`; the sign-in card's state changes announced the same way
4. [ ] `ui/card.tsx`: `CardTitle` renders an `h2` (or `role="heading" aria-level=2`); each card a `section`
       `aria-labelledby` its title
5. [ ] base-ui `RadioGroup`/`Radio` for the `Choice` groups and the mode toggle, styled with `buttonVariants`; the
       "List the shared mailboxes" button out of the radiogroup; the stepper's `aria-current` on the `li`, sr-only
       state, `aria-hidden` separators; the picker option's name and the empty-list `aria-expanded`
6. [ ] Tests (focus targets, status texts, heading levels, arrow keys on a radio group); the front-end checks; an
       axe run on the mock (no `heading-order`); a keyboard-only walk

## Log

- 2026-11-02 — promoted on `task/120-121-window-flow-and-focus` by the main session, with task 120

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
