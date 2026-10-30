---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 074 — CSV edge cases for the bulk writes: BOM, CRLF, quoted fields, duplicates, a malformed last line

## Context

Tests for the `--from-csv` reader of every write command over one shared set of files: a UTF-8 BOM, CRLF and LF
endings, a quoted display name with a comma, surrounding spaces, a header in another case or column order, a
duplicate line (one change, not two), an empty trailing line, and a malformed line as the last one (the whole
file refused before anything is sent, with the line number in the message).
Why: the CSV is the path a support colleague or a script hands a hundred changes through; the documented contract
(docs/development.md, "Writes and plans") promises a whole-file refusal, and Excel on Windows writes BOM and CRLF.
Done when: each case has a test, the reader behaves as documented in every one, and the fixtures live under
`tests/fixtures/csv/` with made-up names.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. The writes follow-ups bundle (064–066) runs in parallel and changes
`PlanResume` and the record format, not `CsvRows`; a reader fix this task needs stays in `CsvRows.cs` and is named
under Deviations.

## Docs that move

- `docs/development.md`, "Writes and plans": the CSV contract sentences follow what the tests pin (BOM, line endings,
  quoting, header case and order, duplicates, the refusal with the line number)
- `tests/fixtures/README.md`: the `csv/` folder and its rules

## Not in this change

- A new CSV column or a new write command
- A CSV written by the product (none exists)

## Steps

1. [x] The fixture files under `tests/fixtures/csv/` (made-up names, `check-fixtures.py` green), one per case
2. [x] The tests in the Cli tests over every write command's `--from-csv`, dry run only; a reader fix where a case
       fails, named under Deviations
3. [x] The docs

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)
- 2026-10-30 — fixtures: `tests/fixtures/csv/edge-cases/<family>/<case>.csv`, 32 files written as bytes by a script
  (so the BOM and CRLF survive an editor; `.gitattributes` keeps `tests/fixtures/**` as `-text`): four families of
  columns (`group`, `mailbox-access`, `forwarding-set`, `forwarding-clear`) times eight cases (`bom-crlf`, `lf`,
  `quoted-comma`, `surrounding-spaces`, `header-case-order`, `duplicate-line`, `trailing-empty-lines`,
  `malformed-last-line`), each over the same two made-up lines that resolve against the recorded answers and the fake
  module. `python -I scripts/check-fixtures.py`: 318 files, 0 findings.
- 2026-10-30 — `tests/M365Aegis.Core.Tests/Operations/CsvEdgeCaseTests.cs`: every reader a write's `--from-csv` calls
  (`GroupWrites.FromCsv`, `MailboxLookups.AccessRowsFromCsv` for grant and for revoke, `ForwardingRowsFromCsv` for set
  and for clear) times every case, 40 cases: the rows as `line|cells…`, or the refusal's exact message, usage and
  "nothing was sent"; plus every family having exactly the eight files, and the `bom-crlf` files starting with
  `EF BB BF` and holding CRLF while the `lf` ones hold no CR. 42 tests, green on the first run: the reader already
  does what the docs say in every case.
- 2026-10-30 — `tests/M365Aegis.Cli.Tests/CsvEdgeCaseCommandTests.cs`: every write with `--from-csv` from the command
  tree (8) times `bom-crlf`, `duplicate-line` and `malformed-last-line`, as child processes under `--dry-run` over every
  layer through the write guard's `GuardRun` (nothing sent, no record): the Excel file and the duplicate plan each line
  exactly once as a change or an unchanged object (lines 2–3, or 2–4 with line 3 never a change, no two equal
  changes); forwarding's duplicate and every malformed last line are exit 2 `usage` with the line(s) in the message and
  the backend `local`. 24 tests, 6.6 s alone.
- 2026-10-30 — docs: `docs/development.md` "The safety tests", the CSV paragraph (what the tests pin: BOM and CRLF or
  LF, quotes, trimming, header case and order, blank lines, duplicates, the refusal with the line);
  `tests/fixtures/README.md`, the bundle's section (the `edge-cases` folder and its rules).

## Deviations from the plan

- No reader fix: every case read as documented on the first run, so `CsvRows.cs` is unchanged.
- The reader's cases run in process in the Core tests (every reader times every case), and the Cli tests run three
  cases per CSV write as child processes (the Excel file, the duplicate, the malformed last line): the plan-level
  behavior and the refusal before anything starts need the command line, the reading itself does not, and the
  in-process tests keep 40 cases in under a second.
- A repeated line in a forwarding file is a usage error naming both lines, as "Writes and plans" already documents
  ("two lines setting one mailbox's forwarding are a usage error"), not one change: a mailbox's forwarding has one
  target. The tests pin that.
- The CSV sentences went into the bundle's own section, "The safety tests", which links to "Writes and plans": the
  round's rule keeps each parallel bundle under its own heading, and the writes bundle edits "Writes and plans" now.

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
