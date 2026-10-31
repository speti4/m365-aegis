---
status: idea
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# A calendar permission entry's holder is matched by its address, not only by its display name

What: `FolderPermissionEntry` carries the holder's primary SMTP address where the backend returns it (the Admin API's
`User` object has `PrimarySmtpAddress`; the sidecar's `Get-MailboxFolderPermission` has the user's `RecipientPrincipal`),
and the take matches the holder to `objects` by address first and by display name only as the fallback.
Why: the take of task 087 matches calendar holders by display name alone, so two people with one name, a renamed
account or a localized `Default`/`Anonymous` entry can land on the wrong object or on none; the access view then misses
a calendar a user reaches.
Done when: over the fixtures a holder with a duplicated display name is matched by address, the `Default` and `Anonymous`
entries still count as everyone's, and the live take's unmatched holder count (in the manifest) drops.
