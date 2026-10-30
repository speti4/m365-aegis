---
status: idea
size: S
origin: done/075-home-folder-acl-test-for-the-cache-and-the-plan-records.md
created: 2026-10-30
milestone: M6 Release and team
---

# The doctor warns when an existing home folder is readable by other users

What: a doctor check on Windows reads the home folder's access list and warns when `Everyone`, `Users` or
`Authenticated Users` can read it (the rule of task 075: the product creates a missing home with a user-only list but
leaves an existing folder as it is), with the `icacls` line that fixes it in the hint.
Why: SECURITY.md names a cache readable by another user as a vulnerability; a home created elsewhere through
`M365AEGIS_HOME` before task 075, or by hand, keeps its inherited list, and the doctor is where a user finds out.
Done when: the check is `pass` on a home the product created, `warn` on a temp home with an inherited `Users: read`
entry, `skipped` off Windows, and the docs' check list names it.
