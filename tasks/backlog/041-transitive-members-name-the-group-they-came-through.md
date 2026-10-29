---
status: idea
size: S
origin: done/033-group-members-transitive.md
created: 2026-10-29
milestone: M1 Foundation
---

# `group members --transitive` names the group a nested member came through

What: a `via` column (and field) on the nested rows of `group members --transitive`: the direct member group
through which the member is reached, found by one `members` request per nested group (batched through `$batch`),
behind the same switch or a `--via` option so the plain transitive list stays one round trip.
Why: `transitiveMembers` does not say which nested group carries a member; the support question after "is she in
the group" is "through which one", and the window's group page and the M5 access view ask the same.
Done when: a nested row names its group from fixtures and live, the request count equals the nested groups plus
one, and the direct list stays unchanged.
