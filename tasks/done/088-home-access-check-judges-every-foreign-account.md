---
size: S
origin: wip/078-doctor-warns-on-a-home-folder-readable-by-others.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 088 — The `home-access` doctor check judges every account that is not the user, SYSTEM or Administrators

## Context

The `home-access` check of task 078 warns on every allow entry with a read right whose account is not the running
user, `SYSTEM` or `Administrators` (today only `Everyone`, `Users` and `Authenticated Users` are judged), so a sandbox
group or another user with `Modify` inherited from the parent folder is named in the detail; the hint stays the one
`icacls` line, which removes the inheritance and leaves the three allowed accounts.
Why: a home created under a shared parent before task 075 can carry any inherited entry, and the doctor is where a
user finds out; the three groups cover the common case, not the shared-machine one.
Done when: a temp home with an inherited entry for a fourth account is `warn` with that account in the detail, the
user-only home stays `pass`, and the fixtures and snapshots of the doctor carry the case.
Tenth round of 2026-10-31, on one branch with tasks 094 and 093; the task session owns the steps below and may reshape
them.

## Docs that move

- `docs/development.md`: the doctor's `home-access` row, what it judges now
- `docs/usage.md`: the `home-access` line of the doctor section if it names the judged accounts

## Not in this change

- A doctor fix that rewrites the access list itself (the hint stays an `icacls` line the user runs)
- The snapshots folder's access list (task 087 inherits the home folder's, which this check covers)

## Steps

1. [x] The rule: every allow entry with a read right whose account is not the running user, `SYSTEM` or
       `Administrators` is judged; the detail names the account and the right
2. [x] Tests over a temp home: an inherited entry for a fourth account is `warn` naming it; the user-only home is
       `pass`; the existing cases of task 078 still hold
3. [x] The doctor fixtures and snapshots carry the case; the docs under *Docs that move*
4. [x] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session
- 2026-10-31 — the rule (`Doctor.CheckHomeAccess`): every allow entry with a read right whose SID is not the running
  user's, `S-1-5-18` or `S-1-5-32-544` is judged; `HomeAccess` carries the user's SID now (`UserSid`) and each
  `AccessEntry` its account name (`Account`, translated by the disk reader, null when the SID does not resolve). The
  detail names each account once, in the order of the list, with the strongest of `full control`, `modify` and `read`
  its entries add up to (`Doctor.RightName`) and `inherited` when none of them is explicit: the three broad groups by
  their English names as before, any other by its account name or its SID. The tail says "every account on this
  machine" when a broad group is among them, else "an account other than you, SYSTEM and Administrators". The pass
  detail is "only you, SYSTEM and Administrators can read it".
- 2026-10-31 — the hint stays the one `icacls` line; its `/remove:g` adds the other accounts that have an explicit
  entry (one would outlive `/inheritance:r`), not the inherited ones: icacls refuses a SID that does not resolve with
  error 1332, which the first run of the real-disk test met on this machine's temp folder (an inherited entry of an
  account that no longer resolves).
- 2026-10-31 — tests: `DoctorTests` (a sandbox group, another user with modify, an unresolved SID, the user's own SID
  in another case and SYSTEM inherited; several entries of one account; the right names), `HomeFolderAclTests` on a
  real temp home under a parent that grants `ALL APPLICATION PACKAGES` read and `NETWORK SERVICE` modify: `warn` naming
  both with their rights, the hint run, then `pass` and the user's own list. The 078 cases hold with the rights in
  their detail. The doctor fixture `warnings.json` carries a fourth account (`CONTOSO\bob (modify, inherited)`), and
  its two snapshots (the table, the envelope) were accepted with it.
- 2026-10-31 — merged in #121 (squash); closed on `main` by the main session; no follow-up of its own

## Deviations from the plan

- The detail names the right of every judged account, the three broad groups included (`Users (read, inherited)`),
  so the 078 wording of the warning and the pass changed with it.
- An inherited account is not named in the hint's `/remove:g` (icacls error 1332 on a SID that does not resolve);
  `/inheritance:r` removes it.

## Verification

- 2026-10-31 — `M365Aegis.Core.Tests -class *DoctorTests -class *HomeFolderAclTests`: 76 passed (the real-disk ACL
  tests run on Windows). The built `m365-aegis.exe doctor --offline` with `M365AEGIS_HOME` at a new folder under this
  machine's `%TEMP%`: `home-access` was `warn` naming two inherited accounts with `modify` (another local user and a
  SID that does not resolve), which the 078 rule passed; the hint run as printed (`Successfully processed 1 files`),
  and the next doctor run said `pass` ("only you, SYSTEM and Administrators can read it"). The full matrix is in the
  pull request.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
