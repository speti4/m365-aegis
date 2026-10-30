---
status: idea
size: S
origin: done/051-plan-type-plan-records-and-the-write-guard.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# `plan list` and `plan show` over the plan records

What: `plan list` reads the records under the home folder's `plans\` (task 051) into a table (when, operation, mode,
changes applied and failed) and `plan show <record>` prints one record as the plan rendering with each change's
result; both read-only, both with the envelope.
Why: the records are the audit trail of every apply, and today only a file browser reads them.
Done when: a temp home with three records lists them newest first, `plan show` renders a partial apply with its
failed change, and a missing record is exit 4.
