---
size: S
origin: done/053-mailbox-permission-and-forwarding-writes.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 059 — `--no-automapping` on a FullAccess grant

## Context

`mailbox access grant … --right FullAccess --no-automapping` runs `Add-MailboxPermission` with
`-AutoMapping:$false` (the parameter joins the write parameter allow-list of task 053 and the fake module), the plan
names the choice in the change, and the CSV of task 054 gets an optional `automapping` column.
Why: a shared mailbox granted to a whole team appears in every member's Outlook without it; the support team's
requests often say "without automapping".
Done when: the flag shows in the plan, the fake module records the parameter, the sidecar refuses any other
`Add-MailboxPermission` parameter as before, and the CSV column is documented.
Part of the Exchange and window bundle (055, 059, 056, 057) of the round of 2026-10-30; the task session owns the
steps below and may reshape them. The live line is a `--dry-run` from the main session's signed-in round; nothing is
applied. The parallel writes bundle (058, 061, 060, 062) adds its own cmdlets to the allow-list and the fake module
under its own comment, and owns "Writes and plans" in `docs/development.md`: this task adds one row to the CSV
columns table there and nothing else in that section.

## Docs that move

- `docs/development.md`, "Writes and plans": the `automapping` column in the CSV columns table (one row)
- `docs/development.md`, "The Exchange Online sidecar": the parameter on the allow-list
- `README.md`: the flag under the bundle's own heading

## Not in this change

- automapping on an existing grant (a FullAccess holder is unchanged; a change of the flag alone is a follow-up if wanted)
- the window's grant card (waits for the colleague's review of 018)

## Steps

1. [x] `--no-automapping` on `mailbox access grant`, FullAccess only (a usage error with another right); the plan
   change names the choice
2. [x] `-AutoMapping` on the write parameter allow-list (`ExoCmdlets`, the sidecar script) and in the fake module,
   which records it; any other parameter still refused
3. [x] The optional `automapping` CSV column (`CsvRows`), the sample CSV and the tests
4. [x] Docs
5. [x] Live line (main session): a `--dry-run` of a grant without automapping

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window bundle (055, 059, 056, 057)
- 2026-10-30 — `AccessChangeRow` carries `AutoMapping` (true by default); `MailboxLookups.AccessRows(…, autoMapping)`
  marks the FullAccess rows of `--no-automapping` and refuses the flag when no FullAccess is among the rights;
  `AccessRowsFromCsv(path, grant)` reads the optional `automapping` column of a grant's file (`CsvRow.Flag`, the
  yes-or-no reader `keepCopy` now uses too, its message unchanged), refuses `false` on a Send As or Send on Behalf line
  and the column on a revoke's file. The plan's change reads `FullAccess for <holder>, without automapping`; a holder
  who has FullAccess already is unchanged with "its automapping is left as it is". The apply sends `AutoMapping =
  false` in `Add-MailboxPermission`'s parameters only when it is false (the service's default is true).
- 2026-10-30 — `AutoMapping` joins `Add-MailboxPermission`'s entry of `ExoCmdlets.WriteParameters` and of the
  script's `$script:WriteParameters` (one line each; `Remove-MailboxPermission` unchanged); the fake module's
  parameter is a `[bool]`, as the real one, and its write record carries it. `--no-automapping` is an option of
  `grant` only, so on `revoke` it is the parser's usage error.
- 2026-10-30 — tests: `Exchange/AutoMappingTests.cs` (10: the rows, the usage error, the CSV sample and three refused
  files, the plan and the apply through a recording runner over the fake module, a grant with automapping that sends
  no parameter, the allow-list), `AutoMappingCommandTests.cs` (4: the dry run and `--yes` over the fake module's
  record, the CSV's lines, the flag beside Send As and on a revoke), and two Pester tests (the record of
  `AutoMapping` false; `AutoMapping` on a revoke and `-Owner` on a grant refused). Sample
  `tests/fixtures/csv/mailbox-grants-automapping.csv`.

- 2026-10-30 — closed by the main session on `main` after #25; the follow-up "change the automapping of an existing FullAccess grant behind one plan" is not filed: a rare request that `mailbox access revoke` then `grant --no-automapping` covers today; to be re-proposed if the support team asks for it

## Deviations from the plan

- the flag is refused when the rights hold no FullAccess, not whenever another right is named: `--right
  FullAccess,SendAs --no-automapping` grants FullAccess without automapping and Send As as usual

## Verification

- 2026-10-30, local (the worktree): `dotnet test -c Release` 775 passed (the 14 new tests of this task included;
  the write snapshots unchanged); `Invoke-Pester -Path tests/sidecar` 39 passed (2 new); PSScriptAnalyzer over the
  sidecar, its tests and `scripts` (Warning, Error): no findings.
- 2026-10-30, live (the main session's signed-in round, this branch, the broker's cached token; nothing applied):
  `mailbox access grant <shared mailbox> --user <the maintainer's everyday account> --right FullAccess
  --no-automapping --dry-run`: exit 0, mode `dry-run`, backend `pwsh+admin-api`, 1 change and 0 unchanged, 0
  warnings, the change's detail `FullAccess for <name> (<upn>), without automapping`, 5854 ms. The same with `--right
  SendAs`: exit 2, `ok` false, backend `local`, 0 ms, no sign-in. The home's `plans\` folder held 0 records before and
  after both.
- 2026-10-30, the bundle's final local matrix (the worktree, after 055's read went back to the sidecar): `dotnet
  restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c Release` (0 warnings), `dotnet test`
  770 passed, `changelog.py --check` up to date, `check-fixtures.py` 275 files 0 findings, `npm ci`, lint, typecheck,
  Vitest 57 passed, build; `Invoke-Pester -Path tests/sidecar` 39 passed; PSScriptAnalyzer (Warning, Error) over the
  sidecar, `tests/sidecar` and `scripts`: no findings; the `windows` job's steps with an empty `M365AEGIS_HOME`:
  `--version` 0, `--json doctor --offline` 0, `--json whoami` 3, `m365-aegis-gui --smoke` 0 (ready after 545 ms,
  top-level window, no sidecar process), `scripts/release.ps1 -DryRun` 0.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
