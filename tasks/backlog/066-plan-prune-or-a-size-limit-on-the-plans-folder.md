---
status: idea
size: S
origin: done/060-plan-list-and-plan-show-over-the-records.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# `plan prune`, or a size limit on the plans folder

What: `plan prune --older-than <days>` (behind the usual `--dry-run`/`--yes` guard) removes old plan records from the
home folder's `plans\`, or the folder keeps a documented size limit and the oldest records go when an apply writes a
new one; `plan list` says how many records there are and how much they take.
Why: since task 062 a record keeps a CSV batch's full text, so the folder grows with every bulk apply and nothing
trims it.
Done when: a temp home with records older and newer than the limit keeps the newer ones, the dry run lists what would
go, and `docs/development.md` names the rule.
