---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
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

1. [ ] The command inventory from `m365-aegis --help` and every subcommand's help, and the card inventory from the
       front end (`gui/web/src`), so nothing is missed; the guide's outline follows a support colleague's day
2. [ ] `docs/usage.md` written: sign-in and profiles, the lookups per domain with one example each, the writes
       (`--dry-run`, `--yes`, the plan record, `plan list`, `plan show`, `--resume`), the window's cards, the doctor's
       checks and what each `warn` or `fail` means, where the data lives (`%LOCALAPPDATA%\m365-aegis\`)
3. [ ] Every example runs over the fixtures (`contoso.com` names only) and `scripts/check-fixtures.py` passes over
       the guide; a check that every command of the help tree is named in the guide (a script or a test, so a new
       command fails it)
4. [ ] The release script ships `docs/usage.md` as the guide (with task 085) and the docs under *Docs that move*

## Log

- 2026-10-30 — promoted on `task/085-signed-release` with task 085

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
