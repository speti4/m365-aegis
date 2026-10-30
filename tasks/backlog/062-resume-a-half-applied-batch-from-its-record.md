---
status: idea
size: S
origin: done/054-bulk-changes-from-a-csv.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# Resume a half-applied batch from its plan record

What: `--resume <record>` on a write command reads a plan record of task 051 whose apply ended partial or was
stopped (an auth, sidecar or allow-list failure skips the rest), re-plans only the changes marked failed or skipped
against the current state, and applies them under the usual `--dry-run`/`--yes` guard, writing a new record that
names the one it continues.
Why: a 100-row batch that stops at row 40 is re-run from a hand-edited CSV today.
Done when: a record with two failed and three skipped changes resumes as a plan of at most five changes, the ones
already applied in the meantime come out unchanged, and the new record links the old one.
