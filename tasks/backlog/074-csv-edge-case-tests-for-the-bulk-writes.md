---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# CSV edge cases for the bulk writes: BOM, CRLF, quoted fields, duplicates, a malformed last line

What: tests for the `--from-csv` reader of every write command over one shared set of files: a UTF-8 BOM, CRLF and
LF endings, a quoted display name with a comma, surrounding spaces, a header in another case or column order, a
duplicate line (one change, not two), an empty trailing line, and a malformed line as the last one (the whole file
refused before anything is sent, with the line number in the message).
Why: the CSV is the path a support colleague or a script hands a hundred changes through; the documented contract
(docs/development.md, "Writes and plans") promises a whole-file refusal, and Excel on Windows writes BOM and CRLF.
Done when: each case has a test, the reader behaves as documented in every one, and the fixtures live under
`tests/fixtures/csv/` with made-up names.
