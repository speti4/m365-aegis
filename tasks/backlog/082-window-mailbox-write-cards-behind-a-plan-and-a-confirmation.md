---
status: idea
size: L
origin: session 2026-10-30
created: 2026-10-30
milestone: M4 Window
---

# The window's mailbox write cards: access grant and revoke, forwarding set and clear, behind a plan and a confirmation

What: the bridge's first write requests and the task cards over them: `mailbox access grant` and `revoke` (FullAccess,
Send As, Send on Behalf, `--no-automapping`), `mailbox forwarding set` and `clear`; each card ends in the `Plan` the
command line prints and a confirmation that stands for `--yes`, the plan record is written as the command line writes
it, and the write runs over the window's warm `ExchangeSession`.
Why: the M4 done-when is a support colleague granting FullAccess on a shared mailbox from the window without
PowerShell; the writes exist behind the command line since task 053, the window has only reads (task 063).
Done when: a FullAccess grant and a forwarding clear run from the window over the fake module with a plan card, a
confirmation and a plan record; a cancelled card sends nothing; the write-guard test of task 079 is green over every
bridge write request (079 is part of this done-when); the sketch of task 018 is followed or amended; `SECURITY.md` names
the bridge messages among the sinks (task 080's follow-up). Decided 2026-10-30: the whole mailbox write set in one task.
