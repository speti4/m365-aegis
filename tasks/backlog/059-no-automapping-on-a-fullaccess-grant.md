---
status: idea
size: S
origin: done/053-mailbox-permission-and-forwarding-writes.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# `--no-automapping` on a FullAccess grant

What: `mailbox access grant … --right FullAccess --no-automapping` runs `Add-MailboxPermission` with
`-AutoMapping:$false` (the parameter joins the write parameter allow-list of task 053 and the fake module), the plan
names the choice in the change, and the CSV of task 054 gets an optional `automapping` column.
Why: a shared mailbox granted to a whole team appears in every member's Outlook without it; the support team's
requests often say "without automapping".
Done when: the flag shows in the plan, the fake module records the parameter, the sidecar refuses any other
`Add-MailboxPermission` parameter as before, and the CSV column is documented.
