---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
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

1. [x] `user-picker.tsx`: a multi-value mode (or a wrapper) that adds a pick to a comma-separated list and keeps a
       typed full address; the single-value mode for Forward to
2. [x] `mailbox-write-cards.tsx`: Users and Forward to through the picker inside the existing `Field` labels; the
       `LiveStatus` text and the Enter-to-plan behavior kept (Enter on an open list picks, Enter on a closed list
       submits)
3. [x] The mailbox field: suggestions from `mailbox-list` (the access card already reads it), with a typed name,
       address or object id still accepted
4. [x] Tests: a pick and a typed address on each card, Enter on open vs closed list, the plan request's payload;
       docs under *Docs that move*; the front-end checks; a keyboard walk of the mock

## Log

- 2026-11-02 — promoted on `task/125-126-write-card-picker-and-small-fixes` by the main session, with task 126
- 2026-11-02 — the combobox of task 112 became `SuggestField` (`components/suggest-field.tsx`): the debounce, the
  dropped old answer, the keys, the list and its note, over a `suggest` function; `UserPicker` fills it from
  `user-search` (its tests unchanged and green), `MailboxPicker` (`components/mailbox-picker.tsx`) from one
  `mailbox-list` with `shared: true` at the first letter, filtered on the page (name, a word of it, address, principal
  name; eight at most), asked only while the card can write, forgotten at a sign-out, asked again after a failure.
  `multiple` (the Users field): the item after the last comma or semicolon is searched, a pick replaces it once with a
  comma after it (`addToList`). `enterPicksFirst` (the write cards, the access changes card): Enter on an open list
  picks the highlighted suggestion or the first; Enter on a closed list submits the form ("See the plan"). The write
  card's `picks` became a function that receives the card's announce, so a list's line reaches the card's status
  region on the picks step and a step change clears it (commit `710c62e`)
- 2026-11-02 — tests: a pick and a typed address on both write cards, Enter on an open and on a closed list, the
  plan request's payload from the picked addresses, one `mailbox-list` for several letters, signed out no request, a
  failed and an unexpected `mailbox-list` reply, eight mailboxes and "more", `addToList` / `currentItem`; Vitest 277
  tests, 100 % on all four counters
- 2026-11-02 — docs: `docs/usage.md` "The window" (Mailbox rights step 1, Mailbox forwarding), `docs/development.md`
  "The front end" (the user picker paragraph: `SuggestField`, `MailboxPicker`, `multiple`, `enterPicksFirst`)
  (commit `a67a60a`)

- 2026-11-02 — merged as #182 (squash); closed on `main` by the main session; no follow-up (the README picture
  is retaken by task 122, which rewrites the same card's strings)

## Deviations from the plan

- The mailbox suggestions are the shared mailboxes only (`mailbox-list` with `shared: true`, as the access card's
  "List the shared mailboxes"), not every mailbox: a tenant-wide `Get-EXOMailbox` for a suggestion list costs too much
  in a large tenant; a user mailbox goes by its typed address, which the note under the list says
- "Enter on an open list picks" is the first suggestion when none is highlighted, on the write cards and the access
  changes card only (`enterPicksFirst`); the two access cards keep task 112's behavior (Enter without a highlight
  submits the text), since there a pick looks the user up at once
- A pick on a write card fills the field and sends nothing; the plan waits for "See the plan" or Enter on a closed list

## Verification

- 2026-11-02 — `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (277 tests, 100 % statements,
  branches, functions, lines), `npm run build` (all `--prefix gui/web`); a keyboard walk of the browser mock (port 5176, after the rebase onto task 127) with Playwright: Mailbox rights "rec" → Reception listed from the shared mailboxes, Enter on the open list picked it; Users "ali" ArrowDown Enter, "bob" Enter → `alice@contoso.com, bob@contoso.com, `; Enter on the closed list → "Plan: 2 changes"; Forwarding: a typed `alice@contoso.com` (no shared mailbox, the note says a full address works), Forward to "bob" listed, a typed outside address went as typed → "Plan: 1 change"; screenshots `125-126-rights-mailbox-list.png`, `125-126-rights-users-list.png`, `125-126-rights-plan.png`, `125-126-forwarding-mailbox-no-match.png`, `125-126-forwarding-to-list.png`, `125-126-forwarding-plan.png` in the session scratchpad

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
