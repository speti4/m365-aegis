---
status: idea
size: S
origin: done/005-group-teams-and-sharepoint-lookups.md
created: 2026-10-29
milestone: M1 Foundation
---

# `group members --transitive`

What: a `--transitive` switch on `group members` that reads `/groups/{id}/transitiveMembers` instead of
`/members`, so nested group membership answers in one call; the row shows the member's type and, where the
endpoint gives it, through which group it is a member.
Why: a group nested in a group is the usual shape of a department's access, and the direct list hides it.
Done when: the switch answers from fixtures and live, and the table marks nested members.
