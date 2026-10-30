---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 054 — Bulk changes from a CSV: one plan over every row

## Context

`--from-csv <file>` on the writes of 052 and 053 (one row per change, the columns documented in the command's help
and in `docs/development.md`), one `Plan` over every row, an apply that continues past a failed row and names each
row's outcome, and a plan record that holds the whole batch.
Why: the support team's changes come as lists (a new team's members, a leaver's grants to remove); one plan over the
list is what the roadmap's M3 asks for.
Done when: a CSV of three mailbox grants runs as a dry run from the fake module with a plan of three changes, the
apply reports one failed row and two applied, and a CSV with a malformed row is refused before anything is sent.
Part of the M3 writes bundle (051, 052, 053, 054) of the round of 2026-10-30; builds on 052 and 053. The roadmap's
M3 "done when" (a bulk grant as a dry run, then an apply, the tenant's audit log showing exactly the planned
changes) is reached in a later round with test objects; this round proves it on the fixtures and the fake module.

## Docs that move

- `docs/development.md`, "Writes and plans": the CSV columns, the batch plan, the partial result
- `README.md`: the batch under the bundle's own heading
- `tasks/roadmap.md`: none, unless the M3 wording moves

## Not in this change

- a CSV export of the lookups (a reader's convenience, a follow-up if wanted)
- a resume of a half-applied batch from its plan record

## Steps

1. [x] The CSV reader (`Core/Operations/CsvRows.cs`): the columns per write, a malformed line refuses the whole file
   before anything is sent
2. [x] One plan over the rows (each change and unchanged object with its line), the apply past a failed line, the
   per-line outcome, the batch's plan record
3. [x] `--from-csv` on the writes of 052 and 053; Core and Cli tests with sample CSVs under `tests/fixtures/csv/`
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the M3 writes bundle (051, 052, 053, 054)
- 2026-10-30 — `CsvRows`: a header naming the columns in any case and order, commas or (with a header of semicolons
  and no comma) semicolons, quoted cells with separators, line breaks and doubled quotes, UTF-8 with or without a BOM
  (anything else refused), blank lines skipped; refused with exit code 2 and `<file>, line N: …`: an empty file, an
  unknown, repeated or missing column, a line with more or fewer cells, an empty required cell, a stray or unclosed
  quote, text after a closing quote, no line under the header; an unknown right or `keepCopy` value and a forwarding
  mailbox on two lines are refused the same way.
- 2026-10-30 — the columns: `group,user` (the four group writes), `mailbox,user,right` (`mailbox access
  grant|revoke`), `mailbox,to[,keepCopy]` (`mailbox forwarding set`), `mailbox` (`clear`); in each command's help and
  in `docs/development.md`. The Cli reads the file before the runner, so a refused file starts no sign-in and no
  sidecar (`meta.backend` `local`); `--from-csv` next to arguments is a usage error. A plan's changes carry `row`; the
  table gains a `line` column and follows the file's order.
- 2026-10-30 — samples: `tests/fixtures/csv/group-members-add.csv`, `mailbox-grants.csv`,
  `mailbox-grants-malformed.csv`, `mailbox-forwarding-set.csv`; tests: `CsvRowsTests` (the reader's cases and every
  refusal), the CSV cases of `GroupWritesTests` and `MailboxWritesTests`, and `WriteCommandTests` (the three-grant
  dry run of three changes, the apply with two applied and one failed, exit 6; the malformed file refused with
  nothing started; a file next to arguments).
- 2026-10-30 — docs: `docs/development.md` "Writes and plans", "A batch from a CSV file" (the columns table, the
  rules, the partial result); `README.md` "Writes behind a plan"; `tasks/roadmap.md` untouched, the M3 wording holds.

## Deviations from the plan

- The reader is new and strict rather than the usage report's lenient parser of task 043, since a file the reader is
  unsure of must be refused, not guessed.
- Semicolon-separated files are read too (a spreadsheet saved under a Hungarian or German locale writes them).

## Verification

- 2026-10-30 — local, against the fake module: `mailbox access grant --from-csv mailbox-grants.csv --yes` exit 6,
  lines 2 and 4 applied, line 3 (the mail contact) `failed: cmdlet-failed`, the record holds the three; a file with a
  line of two cells exit 2 `…, line 3: 2 cell(s), the header has 3` before any sidecar; against the fixtures `group
  members add --from-csv group-members-add.csv --yes` exit 6 (line 2 applied, line 3 unchanged, line 4 forbidden).
- 2026-10-30 — live, `--dry-run` only: `mailbox access grant --from-csv <3 lines>` (a holder's FullAccess and a
  non-holder's Send As on one shared mailbox, a non-holder's Send on Behalf on another) exit 0, 2 changes and 1
  unchanged ("holds FullAccess already"), every entry with its `row`, backend `pwsh+admin-api`, 3 requests, 4699 ms
  (4801 ms wall); a malformed file exit 2 `usage`, backend `local`, nothing started (98 ms wall). Nothing was
  applied.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
