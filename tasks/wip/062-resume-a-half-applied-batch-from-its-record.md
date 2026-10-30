---
size: S
origin: done/054-bulk-changes-from-a-csv.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 062 — Resume a half-applied batch from its plan record

## Context

`--resume <record>` on a write command reads a plan record of task 051 whose apply ended partial or was stopped (an
auth, sidecar or allow-list failure skips the rest), re-plans only the changes marked failed or skipped against the
current state, and applies them under the usual `--dry-run`/`--yes` guard, writing a new record that names the one
it continues.
Why: a 100-row batch that stops at row 40 is re-run from a hand-edited CSV today.
Done when: a record with two failed and three skipped changes resumes as a plan of at most five changes, the ones
already applied in the meantime come out unchanged, and the new record links the old one.
Part of the writes and plan records bundle (058, 061, 060, 062) of the round of 2026-10-30; the task session owns
the steps below and may reshape them; builds on 060's record reading. The live line is a `--resume … --dry-run` over
a record of round 4 from the main session, if one with a failed change exists; nothing is applied. The parallel
Exchange and window bundle (055, 059, 056, 057) adds a `--no-automapping` flag and an `automapping` CSV column to
`mailbox access grant` (`MailboxWrites`, `CsvRows`): a resumed grant carries the choice its record holds, so this
task reads the plan change's shape and does not edit those two files beyond what the resume needs.

## Docs that move

- `docs/development.md`, "Writes and plans": the resume, the record link, what is re-planned
- `README.md`: the bundle's own heading

## Not in this change

- a resume of a record whose operation is `call`
- an edit of the record's changes before the resume (the CSV is the place to edit)

## Steps

1. [x] The record read back as the changes to resume (failed and skipped only); `--resume <record>` on the writes of
   052 and 053, a usage error next to arguments or `--from-csv`, a record of another operation refused
2. [x] The re-plan against the current state (an already applied change comes out unchanged), the guard as usual,
   the new record with `resumes: <record>`
3. [x] Core and Cli tests (two failed and three skipped resume as at most five, the link in the new record, the
   refusals)
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes and plan records bundle (058, 061, 060, 062)
- 2026-10-30 — `Core/Operations/PlanResume.cs`: `Open(home, record)` reads the record (060's `PlanStore.Open`), refuses
  a raw `call` and a record whose changes were all applied (usage, 2) and keeps the pending changes (failed, skipped,
  or without a result in a cut-short record); `EnsureOperation` refuses a record of another write with the command that
  wrote it in the hint; `Narrow(fresh)` keeps of the write's fresh plan the changes and the objects already as asked
  that continue a pending change (by the CSV line when the record has one, else by the action, the target and the
  change), with their steps (`WritePlan.Select`), adds a warning for a pending change the new plan does not name, and
  sets `Plan.Resumes` to the record.
- 2026-10-30 — the Cli: `WriteFlags.On` adds `--resume <record>` to every group and mailbox write (`call` opts out)
  with an option action, `ResumeAction`, which runs in place of the command's own action: the flags (both is a usage
  error), arguments or `--from-csv` beside it (usage), the record, the operation by the command's path
  (`group members add` is `group-members-add`), the profile (a record of another profile is a usage error); then the
  record's arguments without their `--dry-run`, `--yes`, `--json`, `--table` and `--verbose`, plus this run's, parsed
  again by the root command, a CSV file written from the record's text to a temporary folder (removed after) in place
  of the original path, and the command's own action invoked under the resume (`AsyncLocal`), so `WriteRunner` narrows
  its plan and records the original arguments and file. `WriteRunner.Store` now keeps a `--from-csv` file's full path
  and text in every record (`input`). `MailboxWrites.cs`, `CsvRows.cs` and `MailboxCommand.cs` are untouched, so bundle
  A's `--no-automapping` flag and `automapping` column come back with the record's arguments and file.
- 2026-10-30 — tests: `PlanRecordsTests` (the stopped record's two failed and three skipped changes resumed as Dave and
  Eve to add and Bob, Carol and Finance EU unchanged, Alice left out, the new record linking the old; a call record and
  a fully applied one refused; another write's record refused with its command in the hint; a pending change the new
  plan does not name a warning), `PlanCommandTests` (`--resume … --dry-run` 2 changes and 3 unchanged with `resumes`, no
  record written; `--yes` exit 6 with a fourth record that links the third and keeps the original arguments with this
  run's flags; the batch record's mailbox grants replayed from the record's text over the fake module, line 3 alone;
  the refusals before anything starts with backend `local` and no record: arguments, `--from-csv`, another write, every
  change applied, both flags, another profile, `call --resume` (an unknown option), a missing record exit 4).
- 2026-10-30 — docs: `docs/development.md` "Writes and plans" (two rows of the flags table, `resumes` in the `Plan`,
  the record's `input`, "In code", "A resume from a plan record"), `README.md` (the bundle's heading).
- 2026-10-30 — a resume matches a pending change by its rendered text (the action, the target and the change, or the
  CSV line), not by an object id: the record carries no ids of its own, and the text is what every write already
  renders, so the match needs no knowledge of a write's row type. A record edited by hand must keep the exact text; the
  main session's first live check wrote a made-up record whose names were mis-encoded (pwsh captured the exe's UTF-8
  output in the console code page) and the resume answered `up-to-date` with two accurate "the new plan does not name
  it" warnings; with `[Console]::OutputEncoding` set to UTF-8 the second run matched. Matching by object id is a
  follow-up if a rename between the apply and the resume turns out to matter.

## Deviations from the plan

- The resume plans the record's original rows again in full and narrows the plan, rather than planning only the rows
  of the pending changes: it needs no knowledge of a write's row type, so `MailboxWrites` and `CsvRows` (bundle A's this
  round) stay untouched and a flag or a column added later comes back with the record. The cost is the reads of the
  rows the record had applied (a batch of 100 with 60 left reads the 100 again).
- The record keeps a CSV batch's text (`input`), and the resume reads the batch from it, not from the file, which may
  have moved or changed since; a record without one (written before this task) reads the file at the path it names.
- A record of another profile is refused too: a resume against another tenant would plan the changes there.
- `--resume` is an option action on `WriteFlags`, not code in each write command: a new write gets it for free.

## Verification

- 2026-10-30 — the local matrix in the worktree, as in task 058's Verification.
- 2026-10-30 — over the sample records and the recorded answers: `group members add --resume
  20261030T110000.000Z-group-members-add --dry-run` exit 0, 2 changes and 3 unchanged, `resumes` naming the record, no
  record written; `--yes` exit 6 (Dave applied, Eve `forbidden`), the fourth record with `resumes`; `mailbox access grant
  --resume 20261029T100000.000Z-mailbox-access-grant --dry-run` over the fake module exit 0, the one change of line 3.
- 2026-10-30 — live, the main session's signed-in round, dry runs only, over a record made from a dry run's plan of a
  Microsoft 365 group (its two changes marked failed) in the real home: `group members add --resume <record>
  --dry-run` exit 0, backend `graph`, mode `dry-run`, 2 changes, 0 unchanged, 0 warnings, `resumes` naming the record,
  3 request ids, 871 ms, no record written; as a table the same with the `resumes: … (its failed and skipped changes,
  planned again)` line; `group owners add --resume <record>` exit 2 `usage` ("… is a record of group-members-add, not of
  group-owners-add"); arguments beside `--resume` exit 2 `usage`. The folder held the one made-up record during the
  check and none after. Nothing was applied.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
