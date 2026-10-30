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

1. [ ] The fixture files under `tests/fixtures/csv/` (made-up names, `check-fixtures.py` green), one per case
2. [ ] The tests in the Cli tests over every write command's `--from-csv`, dry run only; a reader fix where a case
       fails, named under Deviations
3. [ ] The docs

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
