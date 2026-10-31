---
status: idea
size: M
origin: wip/109-doctor-per-permission-consent-check.md
created: 2026-11-01
milestone: M6 Release and team
---

# The doctor reads the Entra roles from the Graph token's `wids` claim for the permissions table's role column

What: the consent check of task 109 compares scopes only; a second row (or the same row) reads the directory role
template ids of the Graph token's `wids` claim and names the rows of the setup guide's table whose role the signed-in
account lacks.
Why: a colleague with every consent but without the Exchange or Groups role learns the gap only when a command fails;
the table's role column is the one place the tool states what role each command needs.
Done when: over scripted tokens the doctor names a missing role's rows and passes with the roles present, the role
template ids live next to the permission map with a test that holds them to the table, and the live doctor over the
maintainer's token passes.
