---
status: ready
size: M
origin: session 2026-10-30
created: 2026-10-30
milestone: M3 Writes with safety
---

# Mailbox permission and forwarding writes through the sidecar

What: `mailbox access grant|revoke <mailbox> --user <upn> --right FullAccess|SendAs|SendOnBehalf` and
`mailbox forwarding set|clear <mailbox>` as plans of task 051, run by the sidecar (`Add-/Remove-MailboxPermission`,
`Add-/Remove-RecipientPermission`, `Set-Mailbox` with `-GrantSendOnBehalfTo`, `-ForwardingSmtpAddress`,
`-DeliverToMailboxAndForward`); the allow-list grows in `ExoCmdlets.cs` and the script with a D-004 note on the
writes, the fake module gets the cmdlets and Pester covers them, the Admin API stays read-only (its documented
endpoints are reads); the plan reads the current grants first (the lookups of 016 and 044), so it is current → desired.
Why: the roadmap's M3 "done when" is a mailbox permission grant that runs as a dry run, then an apply.
Done when: `mailbox access grant --dry-run` against the fake module skips a grant already held, `--yes` applies
through the sidecar and the fake records the call, the forwarding pair does the same, and the live `--dry-run`
against the tenant lists the real holders without applying anything.
