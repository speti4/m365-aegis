---
status: idea
size: S
origin: done/070-full-line-and-branch-coverage-enforced-in-ci.md
created: 2026-10-30
milestone: M2 Exchange
---

# A sidecar reply line whose `id` is not a string is skipped, not an internal error

What: `SidecarClient.TryReadReply` treats a reply line whose `id` is not a JSON string the way it treats any other
malformed line (skipped, logged), instead of throwing, which today ends the command as an internal error (exit 70).
Why: the coverage round of task 070 found the branch; the shipped script always writes string ids, but the client's
contract is "a bad line never takes the command down", and one branch breaks it.
Done when: a Core test feeds a reply with a numeric `id` and the command's next reply is still read, with a log line
naming the skipped one.
