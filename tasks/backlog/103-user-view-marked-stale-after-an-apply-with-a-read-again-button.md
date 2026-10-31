---
status: idea
size: S
origin: wip/091-access-card-refreshes-after-a-write-card-applies.md
created: 2026-10-31
milestone: M4 Window
---

# The mailbox access card's user view is marked stale after an apply names that user, with a "Read again" button

What: when a write card's apply (task 082's `plan-applied`) names the user whose grants the mailbox access card shows,
the card marks the list as possibly stale and offers a "Read again" button instead of reading on its own; the mailbox
view keeps the automatic refresh of task 091.
Why: the reverse lookup over every mailbox takes tens of seconds (task 043), so task 091 left the user view alone; a
colleague who just granted a right still sees an old list with no sign that it is old.
Done when: over the mock transport an apply for the shown user marks the view and the button sends one reverse lookup,
an apply for another user marks nothing, and a Vitest test holds both.
