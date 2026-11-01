---
size: S
origin: wip/109-doctor-per-permission-consent-check.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M6 Release and team
---

# 110 — The setup guide's "Sign in once and check it" points the first run at the doctor's consent row

## Context

The first-run section of `docs/setup.md` names the doctor's `consent` row (task 109) as the check that the
permissions table was consented in full, and leads from its warning to the consent step of the registration section.
Why: the row was written for the administrator who skipped a table row, and the guide is where that administrator
reads; today the guide's check list predates the row.
Done when: the section names the row, a warn in it points back to the table and the consent step, and the fixture scan
passes.
Twelfth round of 2026-11-01, on one branch with tasks 112 and 113; the task session owns the steps below and may reshape
them. The permission map's test (task 109) reads the guide's permissions tables: the table itself stays as it is.

## Docs that move

- `docs/setup.md`: the "Sign in once and check it" section (the consent row, the way back to the table)
- `docs/usage.md`: only if its doctor paragraph should point at the guide's section

## Not in this change

- The permissions table's rows or columns (the test of task 109 holds the map to them)
- The live verification of the guide's Azure CLI block (task 108)

## Steps

1. [x] The section names the `consent` row as the full-consent check, shows the warn's shape in one line and points at
       the table and the consent step of the registration section
2. [x] `python -I scripts/check-fixtures.py` passes; the 109 permission map test still passes (the tables unchanged)
3. [x] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/112-user-picker-and-window-words` by the main session
- 2026-11-01 — `docs/setup.md`, "Sign in once and check it": the `whoami` paragraph no longer asks to compare the
  tokens with the table by hand; a new paragraph, "Check the consent with the doctor's `consent` row", says what the
  row weighs (each token's permissions against the permissions table), when it passes and when it warns, shows the
  warn line and its `fix` line as the doctor prints them (the renderer snapshot's example, `AuditLog.Read.All`
  missing), and leads back to steps 7 to 9 of the portal registration and the Azure CLI block's
  `Add-DelegatedPermission` and `admin-consent`, then `login` and `doctor` again; a row left out on purpose may stay a
  warning, and the "no token" part points at the `sign-in` row.
- 2026-11-01 — `docs/usage.md` unchanged: its doctor table's `consent` row already links the permissions table.

## Deviations from the plan

- none

## Verification

- 2026-11-01 — `python -I scripts/check-fixtures.py` (515 files, 0 findings) and `--self-test`; `dotnet test` with
  `PermissionMapTests` (task 109) green over the unchanged permissions tables (1590 tests, 0 failed); the anchors
  `#permissions`, `#in-the-microsoft-entra-admin-center` and `#with-the-azure-cli` are the guide's own headings.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (`docs/usage.md` needed none)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
