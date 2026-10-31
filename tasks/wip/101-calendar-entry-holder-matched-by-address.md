---
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 101 — A calendar permission entry's holder is matched by its address, not only by its display name

## Context

`FolderPermissionEntry` carries the holder's primary SMTP address where the backend returns it (the Admin API's
`User` object has `PrimarySmtpAddress`; the sidecar's `Get-MailboxFolderPermission` has the user's `RecipientPrincipal`),
and the take matches the holder to `objects` by address first and by display name only as the fallback.
Why: the take of task 087 matches calendar holders by display name alone, so two people with one name, a renamed
account or a localized `Default`/`Anonymous` entry can land on the wrong object or on none; the access view then misses
a calendar a user reaches.
Done when: over the fixtures a holder with a duplicated display name is matched by address, the `Default` and `Anonymous`
entries still count as everyone's, and the live take's unmatched holder count (in the manifest) drops.
Eleventh round of 2026-10-31, on one branch with tasks 097, 099 and 100; the task session owns the steps below and may
reshape them. The live unmatched count before and after is the main session's.

## Docs that move

- `docs/usage.md`: the `mailbox calendar` (folder permissions) table, if the address becomes a column

## Not in this change

- The access view's reading of the calendar rows (task 084), beyond what a better match gives it for free
- A holder that is a group: matched by its address like a user, no expansion

## Steps

1. [ ] `FolderPermissionEntry` gets the holder's address from both backends (the Admin API's `User.PrimarySmtpAddress`,
       the sidecar's `RecipientPrincipal`), the fixtures carry it, the output contract's snapshot tests are updated
2. [ ] The take matches the holder by address first, by display name as the fallback; `Default` and `Anonymous` stay
       everyone's; the manifest's unmatched count stays
3. [ ] A fixture with a duplicated display name is matched by address; the Core tests hold it
4. [ ] The live unmatched holder count before and after in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the unmatched holder count of the manifest before and after>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
