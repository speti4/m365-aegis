---
status: idea
size: S
origin: done/063-window-mailbox-access-over-the-layers.md
created: 2026-10-30
milestone: M4 Window
---

# The window's user lookup over the split read of task 043

What: the window's `mailbox-access` request for a user (which mailboxes a user can open or send as) runs the reverse
lookup the way the command line does since task 043, the FullAccess read split across several sidecars, instead of one
bulk read on the window's single warm session.
Why: a live user lookup in the window took 262 s on 2026-10-30 (one sidecar over every mailbox), while the command
line's split read takes 41 s over 620 mailboxes; four minutes is unusable for the support team.
Done when: the window's user lookup answers in the command line's time over the same tenant, the `window:` log line
names the number of sidecars, and the warm session for the mailbox direction is kept.
