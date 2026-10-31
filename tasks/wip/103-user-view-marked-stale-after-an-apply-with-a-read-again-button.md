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

1. [x] The stale mark on the user view when a `plan-applied` names the shown user (`applied-writes.ts`,
       `mailbox-access-card.tsx`), the "Read again" button that sends one reverse lookup, no mark for another user
2. [x] Vitest holds both cases over the mock transport, 100% coverage held; Biome and the typecheck clean
3. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session
- 2026-10-31 — `applied-writes.ts`: an `AppliedWrite` also carries `users`, the users the write card asked for and the
  addresses in its changes' details (`detailAddresses`: "FullAccess for Bob Example (bob@contoso.com)"); `namesUser`
  matches them to the shown user's principal name or display name in any case. `mailbox-write-cards.tsx` keeps the
  request's users beside its mailbox and tells them with the apply
- 2026-10-31 — `mailbox-access-card.tsx`: while the card shows a user's grants, a grant or a revoke that names the user
  sets `stale` on the result: a notice "A change on the Mailbox rights card named this user since this list was read,
  so it may be out of date. Reading it again goes over every mailbox and takes a while." with "Read again", which sends
  one `mailbox-access` with `{"user": …}` and is disabled while a lookup runs; the new answer clears the mark. The
  mailbox view's automatic refresh of task 091 is unchanged; a forwarding or an apply for another user marks nothing
- 2026-10-31 — docs: `docs/usage.md` "Mailbox access" (a paragraph of its own), `docs/development.md` "The user view's
  stale mark"

## Deviations from the plan

- The channel's message gains `users` rather than a second channel: the card that listens already filters by the
  operation, and the users come from the request and the plan, as the mailbox and the addresses do
- `settle()` of the DOM tests waits a few more turns of the clock (task 102's Deviations): the chain apply, then the
  access card's request, failed now and then on `main` already

## Verification

- 2026-10-31 — Vitest `applied-writes.test.tsx`, "the mailbox access card's user view after an apply (task 103)": a grant
  for bob marks bob's list and sends no request, a second apply sends none, "Read again" sends exactly one
  `mailbox-access` `{"user": "bob@contoso.com"}` and shows the new FullAccess on Finance; a revoke for bob marks it; a
  grant for alice and a forwarding to bob mark nothing and send nothing; a user named by the plan's detail address
  alone marks it, and "Read again" is disabled while its lookup runs. `detailAddresses` and `namesUser` unit cases. The
  task 091 cases of the file unchanged and green; the suite 194 tests, coverage 100% on all four measures

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
