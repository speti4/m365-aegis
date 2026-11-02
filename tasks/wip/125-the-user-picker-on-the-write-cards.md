---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 125 — The user picker on the write cards

## Context

The Users field of the mailbox permissions card and the Forward to field of the forwarding card use the
`UserPicker` the access cards already have; a pick is added to the comma-separated list, a typed full address
still passes through. The mailbox field offers suggestions from the `mailbox-list` reply the window already has.
Why: the UX/UI review of 2026-11-02 (finding U-3): the picker exists and works, but the screen where a wrong name
costs most, a change to the tenant, is the one that asks for free text.
Decided 2026-11-02: bundled with task 126 on one branch (both on the write cards and the diff card). Tasks 120, 121,
123 and 124 are merged: the write cards are a form with labeled fields (`Field` in `card-parts.tsx`), a `Choice`
radio group, a `LiveStatus` region and `focusTarget` focus rules; keep those.
Done when: both write cards pick a user from the directory or the snapshot in the mock, the plan is computed from
the picked addresses, and the tests cover a pick and a typed address.

## Docs that move

- `docs/usage.md`, "The window": the Users and Forward to fields pick from the directory or the snapshot; the
  mailbox suggestions
- `docs/development.md`, "The front end", if it lists the picker's users

## Not in this change

- The vocabulary (task 122) and the typography / change ticket (task 127)
- A lookup → change bridge (a Revoke button on an access row), not chosen in the review round
- Any change to the bridge protocol: the picker's `user-search` and the `mailbox-list` messages already exist

## Steps

1. [ ] `user-picker.tsx`: a multi-value mode (or a wrapper) that adds a pick to a comma-separated list and keeps a
       typed full address; the single-value mode for Forward to
2. [ ] `mailbox-write-cards.tsx`: Users and Forward to through the picker inside the existing `Field` labels; the
       `LiveStatus` text and the Enter-to-plan behavior kept (Enter on an open list picks, Enter on a closed list
       submits)
3. [ ] The mailbox field: suggestions from `mailbox-list` (the access card already reads it), with a typed name,
       address or object id still accepted
4. [ ] Tests: a pick and a typed address on each card, Enter on open vs closed list, the plan request's payload;
       docs under *Docs that move*; the front-end checks; a keyboard walk of the mock

## Log

- 2026-11-02 — promoted on `task/125-126-write-card-picker-and-small-fixes` by the main session, with task 126

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
