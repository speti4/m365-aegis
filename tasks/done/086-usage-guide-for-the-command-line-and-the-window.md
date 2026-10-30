---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 086 — A usage guide for the command line and the window, shipped in the release zip

## Context

`docs/usage.md` for the support team: the sign-in, the daily lookups per domain with examples, the writes with
`--dry-run` and `--yes`, the plan records and a resume, the window's cards, the doctor's checks and what each failure
means; the release zip of task 085 carries it, so the guide is written for a reader who has the zip and no repo.
Why: the README is written for a contributor; the M6 done-when needs a guide a support colleague reads once.
Done when: the guide covers every command of `m365-aegis --help` and every card of the window, every example runs over
the fixtures, and `scripts/check-fixtures.py` passes over it (no tenant data in the examples).
Ninth round of 2026-10-30, with task 085 on one branch (the release script ships this file as the guide instead of the
README); the task session owns the steps below and may reshape them.

## Docs that move

- `docs/usage.md`: new
- `README.md`: points the support reader at the guide; the contributor text stays
- `docs/development.md`, "The release": the guide file that ships
- `CONTRIBUTING.md`, if it lists the docs to update: the guide joins the list for a command or card change

## Not in this change

- The onboarding of the support team (installation on their machines, the first sign-in on a new machine)
- Screenshots of the window (text only; a screenshot carries tenant data too easily)

## Steps

1. [x] The command inventory from `m365-aegis --help` and every subcommand's help, and the card inventory from the
       front end (`gui/web/src`), so nothing is missed; the guide's outline follows a support colleague's day
2. [x] `docs/usage.md` written: sign-in and profiles, the lookups per domain with one example each, the writes
       (`--dry-run`, `--yes`, the plan record, `plan list`, `plan show`, `--resume`), the window's cards, the doctor's
       checks and what each `warn` or `fail` means, where the data lives (`%LOCALAPPDATA%\m365-aegis\`)
3. [x] Every example runs over the fixtures (`contoso.com` names only) and `scripts/check-fixtures.py` passes over
       the guide; a check that every command of the help tree is named in the guide (a script or a test, so a new
       command fails it)
4. [x] The release script ships `docs/usage.md` as the guide (with task 085) and the docs under *Docs that move*

## Log

- 2026-10-30 — promoted on `task/085-signed-release` with task 085
- 2026-10-30 — the inventory: the built exe's help tree, walked command by command (`--help` at every level), is 47
  commands under 15 top-level ones (`probe` is hidden and left out); the front end's cards are five, from
  `gui/web/src/App.tsx` and `components/`: Sign-in, Group members, Mailbox access, and the write cards of task 082,
  Mailbox rights and Mailbox forwarding (the plan view, the confirmation and the result shared by both).
- 2026-10-30 — `docs/usage.md`: before the first start (the runtimes with their `winget` lines, the doctor, checking
  the download: the checksum, the attestation, `Unblock-File`, the signature), the sign-in and the profiles, how the
  answers look, the lookups per domain (groups, Teams and SharePoint, mailboxes, mail flow, sign-ins and audit, `call`)
  with an example each, the changes (`--dry-run`, no flag and exit 7, `--yes`, the writes, a CSV batch, the plan
  records with `plan list`, `plan show` and `plan prune`, `--resume`), the window's five cards with the write cards'
  four steps and their two refusals, the doctor's rows with what each `warn` and `fail` means, where the data lives,
  and the exit codes. Every name is a fixture's (`contoso.com`, `fabrikam.com`, the group Finance, the mailboxes
  finance, reception, alice and bob, the sample plan records).
- 2026-10-30 — the check: `UsageGuideTests` in the Cli tests (a test rather than a script, so the `dotnet` job runs it
  with no new step): every command path of `Program.Build()` (hidden ones aside) appears as `m365-aegis <path>`; every
  card title (`<CardTitle>` text or a `WriteCard` title under `gui/web/src/components`) has a `### <title>` section;
  every example line of the `powershell` blocks runs as a child process over the recorded Graph and Admin API answers,
  the fake Exchange Online module and a home with the sample plan records, and answers 0, or 7 for a write without a
  flag. `--yes` runs as `--dry-run` (the write-guard tests own the apply), a `--from-csv` file name is the sample's
  under `tests/fixtures/csv`, and the examples that need the real sign-in or the network (`login`, `logout`, `whoami`,
  `profile remove`, `doctor` without `--offline`) are only named. A misspelled command and a group the fixtures do not
  know failed it when tried (then put back). `scripts/check-fixtures.py` now also reads `docs/usage.md`.
- 2026-10-30 — `release.ps1` copies `docs/usage.md` as `GUIDE.md`; the README has a "Using a release" section and a
  Documentation line for the guide; CONTRIBUTING.md says a new command, example or card changes the guide and the
  tests hold it; docs/development.md describes the guide's tests and names it in the layout and the release.
- 2026-10-30 — merged in #114 (squash) with task 085; the follow-up it found (the fixture scan misses two-label
  domains) approved and filed as task 094. Closed on `main` by the main session.

## Deviations from the plan

- "Every example runs over the fixtures": a write's `--yes` example runs as `--dry-run` in the test, and the five
  sign-in and network examples are named, not run; the reasons are in the test's `NotRun` list and in
  docs/development.md.
- The card check is part of the same C# test (from the components' source), not a front-end test.

## Verification

- 2026-10-30: `dotnet test` of `UsageGuideTests`: 105 cases passed (the inventory, 47 commands named, 5 cards with
  their section, 52 distinct example lines of the 54, of which the sign-in and network ones are skipped by name); with `team lisst` and `mailbox
  group-members nobody` put into the guide for the try, the cases failed with exit 2 and 4, and `team list` was missing.
- 2026-10-30: `python -I scripts/check-fixtures.py` over the guide: 331 files, 0 findings.
- 2026-10-30: the release zip of a local signed release holds `GUIDE.md` starting "# m365-aegis — the usage guide".
- 2026-10-30: the full local matrix with task 085 (the PR's checklist): 1443 tests passed, the coverage gate at 100%
  lines and branches of the Core and the command line, the changelog check.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-30` by the main session after the merge
