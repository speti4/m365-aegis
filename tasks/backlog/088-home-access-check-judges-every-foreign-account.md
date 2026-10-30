---
status: idea
size: S
origin: wip/078-doctor-warns-on-a-home-folder-readable-by-others.md
created: 2026-10-30
milestone: M6 Release and team
---

# The `home-access` doctor check judges every account that is not the user, SYSTEM or Administrators

What: the `home-access` check of task 078 warns on every allow entry with a read right whose account is not the
running user, `SYSTEM` or `Administrators` (today only `Everyone`, `Users` and `Authenticated Users` are judged), so
a sandbox group or another user with `Modify` inherited from the parent folder is named in the detail; the hint stays
the one `icacls` line, which removes the inheritance and leaves the three allowed accounts.
Why: a home created under a shared parent before task 075 can carry any inherited entry, and the doctor is where a
user finds out; the three groups cover the common case, not the shared-machine one.
Done when: a temp home with an inherited entry for a fourth account is `warn` with that account in the detail, the
user-only home stays `pass`, and the fixtures and snapshots of the doctor carry the case.
