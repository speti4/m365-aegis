---
size: S
origin: wip/078-doctor-warns-on-a-home-folder-readable-by-others.md
created: 2026-10-30
started: 2026-10-31
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

1. [ ] The rule: every allow entry with a read right whose account is not the running user, `SYSTEM` or
       `Administrators` is judged; the detail names the account and the right
2. [ ] Tests over a temp home: an inherited entry for a fourth account is `warn` naming it; the user-only home is
       `pass`; the existing cases of task 078 still hold
3. [ ] The doctor fixtures and snapshots carry the case; the docs under *Docs that move*
4. [ ] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
