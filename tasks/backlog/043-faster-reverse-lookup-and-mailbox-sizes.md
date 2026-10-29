---
status: idea
size: M
origin: done/016-mailbox-access-reverse-lookup-and-forwarders.md
created: 2026-10-29
milestone: M2 Exchange
---

# A faster `mailbox access --user` and `mailbox size` over every mailbox

What: the live round of 2026-10-29 measured `mailbox access --user <upn>` at 138 s and `mailbox size --top 5` at
80 s over 620 mailboxes, about 0.2 s and 0.12 s per mailbox, one request each through the piped `Get-EXO*`
cmdlets. Try, in this order: `Get-EXOMailboxPermission` without an identity (`-ResultSize Unlimited`, if it lists
every mailbox's entries in paged calls), the piped mailboxes split across several sidecars at once, and Graph's
mailbox usage report (one call, about a day old) for the sizes; measure each live.
Why: the reverse lookup is the mailbox question the tool exists for, and two minutes is not a question a support
colleague asks twice.
Done when: both commands answer the same tenant in well under a minute, or the Log states the floor of each
cmdlet and the hint names it.
