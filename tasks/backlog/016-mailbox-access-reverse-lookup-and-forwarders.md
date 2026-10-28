---
status: idea
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M2 Exchange
---

# Mailbox access in both directions, external forwarders, shared mailboxes and sizes

What: `mailbox access <mailbox>` (who holds FullAccess, SendAs and Send on Behalf on it) and `mailbox access
--user <upn>` (which mailboxes that user can open or send as, the reverse lookup over every mailbox through the
`Get-EXO*` bulk cmdlets), `mailbox forwarders` (every mailbox with `ForwardingSmtpAddress` or
`DeliverToMailboxAndForward` set, external ones flagged), `mailbox list --shared` and `mailbox size`
(`Get-EXOMailboxStatistics`); each through the sidecar and the output contract.
Why: the reverse lookup and the forwarder list are the mailbox questions the admin centers do not answer and the
paid tools charge for (the research's "real gap").
Done when: each command answers from the fake module in the tests and live after one sign-in, and the reverse
lookup over the tenant finishes within the sidecar's throttling budget.
