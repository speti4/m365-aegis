---
size: S
origin: done/075-home-folder-acl-test-for-the-cache-and-the-plan-records.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 078 — The doctor warns when an existing home folder is readable by other users

## Context

A doctor check on Windows reads the home folder's access list and warns when `Everyone`, `Users` or
`Authenticated Users` can read it (the rule of task 075: the product creates a missing home with a user-only list but
leaves an existing folder as it is), with the `icacls` line that fixes it in the hint.
Why: SECURITY.md names a cache readable by another user as a vulnerability; a home created elsewhere through
`M365AEGIS_HOME` before task 075, or by hand, keeps its inherited list, and the doctor is where a user finds out.
Done when: the check is `pass` on a home the product created, `warn` on a temp home with an inherited `Users: read`
entry, `skipped` off Windows, and the docs' check list names it.
Seventh round of 2026-10-30, on its own branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, the doctor's check list: the new check, its three outcomes and the hint
- `README.md`, the doctor section, if it lists the checks
- `SECURITY.md`: the doctor check named next to the home folder rule, if the file names the safeguards

## Not in this change

- Fixing the access list from the doctor (the hint carries the `icacls` line, the user runs it)
- The MSAL cache file's own access list beyond the folder's

## Steps

1. [ ] A doctor check reads the home folder's access list on Windows and warns when `Everyone`, `Users` or
       `Authenticated Users` has a read right, with the `icacls` fix in the hint; `skipped` off Windows
2. [ ] Core tests: `pass` on a home the product created (task 075's helper), `warn` on a temp home with an inherited
       `Users: read` entry, `skipped` off Windows (the platform behind an injectable seam if needed)
3. [ ] `doctor --offline` still runs the check (no sign-in needed); snapshots updated where the rendering changed
4. [ ] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/078-doctor-home-folder-acl`

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
