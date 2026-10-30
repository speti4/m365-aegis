---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M3 Writes with safety
---

# Bulk changes from a CSV: one plan over every row

What: `--from-csv <file>` on the writes of 052 and 053 (one row per change, the columns documented in the command's
help and in `docs/development.md`), one `Plan` over every row, an apply that continues past a failed row and names
each row's outcome, and a plan record that holds the whole batch.
Why: the support team's changes come as lists (a new team's members, a leaver's grants to remove); one plan over the
list is what the roadmap's M3 asks for.
Done when: a CSV of three mailbox grants runs as a dry run from the fake module with a plan of three changes, the
apply reports one failed row and two applied, and a CSV with a malformed row is refused before anything is sent.
