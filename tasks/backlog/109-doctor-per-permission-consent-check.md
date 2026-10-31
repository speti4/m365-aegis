---
status: idea
size: M
origin: wip/106-user-facing-readme-and-setup-guide.md
created: 2026-10-31
milestone: M6 Release and team
---

# Doctor: a per-permission consent check that names the missing row of the setup guide's table

What: the doctor's `sign-in` row today only says that a token is missing; a new check compares the permissions each
token carries (what `whoami` lists) with the ones the commands need, and names the rows of the setup guide's
permissions table that are not consented yet, with the consent as the fix line.
Why: an administrator who skipped a row learns it only when a command exits 8; the doctor is the first thing a
colleague runs, and the table is the one list of what the tool asks for.
Done when: with a token that lacks a consented permission the doctor warns with the row's name, with every permission
consented it passes, and the offline tests cover both over scripted tokens. A plan session decides the shape (the
command-to-permission map in the Core, kept in step with the table by a test).
