---
status: ready
size: M
origin: session 2026-10-30
created: 2026-10-30
milestone: M3 Writes with safety
---

# Group membership and owner writes behind a plan

What: `group members add|remove <group> <user…>` and `group owners add|remove <group> <user…>` through Graph
(`POST /groups/{id}/members/$ref`, `DELETE /groups/{id}/members/{id}/$ref`, the owners alike), each a `Plan` of
task 051: the plan lists only what is not already so (a member already present is unchanged), warns when the last
owner would go, and the apply reports per change; recorded POST and DELETE answers with their `.headers` files
under `tests/fixtures`.
Why: the first write of M3 on the Graph side; group membership is the support team's most common change (D-001).
Done when: `group members add --dry-run` from fixtures prints a plan with one change and one unchanged member,
`--yes` applies it from the recorded answers, the last-owner warning shows, and the live `--dry-run` against the
tenant lists the real current state without applying anything.
