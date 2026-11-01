---
size: S
origin: wip/087-snapshot-store-and-snapshot-take.md
created: 2026-10-31
started: 2026-10-31
closed: 2026-11-01
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

1. [x] `FolderPermissionEntry` gets the holder's address from both backends (the Admin API's `User.PrimarySmtpAddress`,
       the sidecar's `RecipientPrincipal`), the fixtures carry it, the output contract's snapshot tests are updated
2. [x] The take matches the holder by address first, by display name as the fallback; `Default` and `Anonymous` stay
       everyone's; the manifest's unmatched count stays
3. [x] A fixture with a duplicated display name is matched by address; the Core tests hold it
4. [x] The live unmatched holder count before and after in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/097-snapshot-take-exchange-and-sharing` by the main session
- 2026-11-01 — the note splits the unmatched holders: `N holder(s) matched no object: X without an address, Y with an
  address outside the snapshot`, an entry's address or a holder named by an address counting as the latter, so the
  administrator knows what to look for
- 2026-10-31 — the manifest gained the count first: the folderPermissions domain's note says `N holder(s) matched no
  object` (`Default` and `Anonymous` left out), in the build the live baseline ran
- 2026-10-31 — `FolderPermissionEntry.Address`: the `PrimarySmtpAddress` of the Admin API's `User` object
  (`Exchange.MailboxFolderUserId`, as the recorded reception answer has it; null for `Default` and `Anonymous`), or a
  `User` given as an address. The take matches the holder by that address first (user principal name, then a unique
  address or name), by the display name only without one; `Default` and `Anonymous` match nothing. `mailbox
  folder-permissions` shows the address next to the name (`Alice Example <alice@contoso.com>`) and the JSON carries
  `address`. The calendar fixtures carry the addresses; `SnapshotEdgeTests` hold two users of one display name matched
  by address, an address no object has falling back to the name, a holder given as an address, and one no object
  answers to, counted in the note
- 2026-11-01 — merged in #151 (squash); closed on `main` by the main session: the localized `Default`/`Anonymous` follow-up is not filed (the maintainer's call: the live take had 621 `Default` and 616 `Anonymous` entries by their English names and no other shape; a task when another tenant shows one)

## Deviations from the plan

- The sidecar's `RecipientPrincipal` is not read: the sidecar does not run `Get-MailboxFolderPermission` (it is on the
  Admin API only, not on the sidecar's allow-list), so the Admin API's `User.PrimarySmtpAddress` is the one source
- The done-when's "the live unmatched count drops" was a wrong expectation for this tenant: the 64 unmatched
  holders of the baseline stayed 64. Live, the display-name holders carry no address in the Admin API's answer (so
  in the mailbox checked; most likely removed accounts whose entries linger), and the address-like ones are addresses
  no user or group of the snapshot has, so no match rule can place them. The duplicated-display-name
  case stays proven over the fixtures; the address match works where the service gives an address (a live
  `mailbox folder-permissions` showed entries with and without it). The note now says which kind each is
- A localized `Default`/`Anonymous` entry is still told by its name: the recorded answer names them `Default` and
  `Anonymous` with no address, and nothing recorded shows a localized one

## Verification

- 2026-11-01 — live, the main session, cached token: the folderPermissions note of the baseline take (build `5a30bc7`,
  match by name) counted 64 holders matched to no object; the after-take (build `46118d7`, match by address first)
  counted 64 again, some 40 of them display names without an address in the Admin API's answer and some 25 addresses
  no object of the snapshot has, of some 40 distinct holders; `Default` and `Anonymous` (some 1,200 rows) counted as
  everyone's, as before. A live `mailbox folder-permissions` of one affected mailbox from build `46118d7` listed
  entries with an `address` and the unmatched one without
- 2026-11-01 — local, merged tree: the matrix of task 097's Verification; the split of the note is in
  `SnapshotEdgeTests` (one holder without an address, two with an address outside the snapshot)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposed them and filed the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-11-01` by the main session after the merge
