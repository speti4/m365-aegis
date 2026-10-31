---
status: idea
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# The calendars' folder names in one piped `Get-EXOMailboxFolderStatistics` call

What: the folderPermissions domain of the take finds each mailbox's calendar folder name (the mailbox's language names
it) from one piped call over every mailbox (`Get-EXOMailbox | Get-EXOMailboxFolderStatistics -FolderScope Calendar`)
instead of one call per mailbox, measured live before the change.
Why: 620 sidecar calls a take, likely the slowest Exchange part of the live take of 2026-10-31; the folder names change
rarely and come from one cmdlet.
Done when: the live measurement of the per-mailbox and the piped read is in the Verification, the fake module's take
gives the same calendar rows, and the domain's `read` label names the piped form.
