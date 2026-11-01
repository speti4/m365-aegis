---
status: ready
size: S
origin: wip/100-calendar-names-in-one-piped-folder-statistics-call.md
created: 2026-11-01
milestone: M5 Snapshot and views
---

# The folderPermissions domain of the take reads the calendar permissions in parallel

What: the per-mailbox `Get-MailboxFolderPermission` reads over the Admin API run several at a time, as the grants domain
splits its reads over sidecars, with a concurrency the take's budget bounds and backs off from on a throttled answer.
Why: after tasks 097, 099 and 100 the calendar permissions are most of the incremental take (about 4.5 minutes of 7.5
over some 600 mailboxes in the live run of 2026-11-01), one Admin API call per mailbox in sequence.
Done when: the fake module's take gives the same rows, the live domain time drops measurably without a throttled answer
(a before/after in the Verification), and the `requests` column of the take still counts every call.
