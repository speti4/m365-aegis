---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 120 — Lookup errors shown in the card, labeled fields, Enter and Escape on the write cards, and a complete confirmation step

## Context

A failed lookup shows its error in the card (the write cards' `Notice` and `ErrorText` parts), clears or dims the
old answer and marks the field invalid; the page-level toast keeps a close button. A visible label above every
field (Mailbox, Users, Forward to, From, To); the write cards' fields sit in a form, so Enter means "See the plan"
and Escape goes back a step. The confirmation shows what the plan showed (the action badge, the from → to line,
the warnings, the account the change runs as), its button names the action (grant, revoke, start or stop
forwarding; destructive for a removal), and a Cancel returns to the first step.
Why: the UX/UI review of 2026-11-02 (findings U-1, D-3, A-4; U-3, D-7, A-8, U-8, A-10; U-5, U-6, C-2, D-2): after a
misspelled name the previous group's members stay on the card as the answer, a filled write card does not say which
field is the mailbox, and the last look before a change shows less than the plan did.
Decided 2026-11-02: bundled with task 121 on one branch, because both rewrite the lookup cards, the write cards and
`plan-view.tsx`.
Done when: a keyboard walk of the mock shows the error under the field and no stale table, labels, Enter and Escape
on the write cards, a confirmation that names the action and the account; the tests and usage.md "The window" match.

## Docs that move

- `docs/usage.md`, "The window": the error under the field, the labels, the confirmation's content and its button
  names, Cancel
- the front-end tests (`gui/web/src/components/*.test.tsx`) for the quoted strings and the new behaviors

## Not in this change

- The vocabulary sweep (task 122): only the strings this task introduces are new; existing terms stay as they are
- The status color tokens (task 123): a destructive button uses the existing `destructive` variant
- The sticky top bar and the account in the header (task 124); the confirmation names the account from the sign-in
  state the page already has
- The user picker on the write cards (task 125)

## Steps

1. [ ] Lookup cards (`group-members-card.tsx`, `mailbox-access-card.tsx`, `user-access-card.tsx`,
       `snapshot-diff-card.tsx`): the error inline under the form with `Notice`/`ErrorText`, the old result cleared or
       dimmed when a new lookup starts, `aria-invalid` + `aria-describedby` on the field; `App.tsx`: the toast with a
       close button, kept for page-level errors only
2. [ ] A `Label` part (shadcn, one file under `components/ui/`) and a visible label above every field of every card
3. [ ] `mailbox-write-cards.tsx`: the picks inside a `<form onSubmit>` (Enter = "See the plan"), Escape = back on the
       plan and confirm steps (not while applying)
4. [ ] `plan-view.tsx` `ConfirmView`: reuse `ChangeLine` (action badge, from → to), the warnings in compact form, the
       account and profile the change runs as, the button named by the operation, destructive for a removal, Cancel
5. [ ] Tests for each; docs under *Docs that move*; the front-end checks; a keyboard walk of the mock

## Log

- 2026-11-02 — promoted on `task/120-121-window-flow-and-focus` by the main session, with task 121

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
