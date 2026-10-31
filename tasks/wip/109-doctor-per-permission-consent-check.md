---
size: M
origin: wip/106-user-facing-readme-and-setup-guide.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 109 — Doctor: a per-permission consent check that names the missing row of the setup guide's table

## Context

The doctor's `sign-in` row today only says that a token is missing; a new check compares the permissions each
token carries (what `whoami` lists) with the ones the commands need, and names the rows of the setup guide's
permissions table that are not consented yet, with the consent as the fix line.
Why: an administrator who skipped a row learns it only when a command exits 8; the doctor is the first thing a
colleague runs, and the table is the one list of what the tool asks for.
Done when: with a token that lacks a consented permission the doctor warns with the row's name, with every permission
consented it passes, and the offline tests cover both over scripted tokens.
Eleventh round of 2026-10-31, on one branch with task 095; the task session plans the shape itself (the backlog item
asked for a plan session): the command-to-permission map lives in the Core and a test keeps it in step with the setup
guide's table. The live run over the maintainer's real token is the main session's.

## Docs that move

- `docs/setup.md`: the permissions table, if its shape has to carry what the test reads (a stable column, an anchor)
- `docs/usage.md`: the doctor's check list gets the consent row
- `docs/development.md`: the doctor section, the map and the test that holds it to the table

## Not in this change

- Any change to what the commands ask for (the scopes of task 002)
- A consent flow started by the doctor (the fix line names it, the administrator runs it)
- The window's sign-in hints (task 057), beyond what the doctor's new row gives the command line

## Steps

1. [ ] The shape: the command-to-permission map in the Core (Graph scopes and the Exchange roles, as `docs/setup.md`'s
       table lists them), a test that reads the table and holds the map to it; the step's design in the Log
2. [ ] The check in `Doctor.cs`: the permissions of each cached token against the map, a warning that names the
       missing table rows with the consent as the fix line, a pass when every one is consented, a plain line when no
       token is cached (today's `sign-in` row stays)
3. [ ] The offline tests over scripted tokens: a missing permission warns with the row's name, a full set passes; the
       output contract's snapshot tests of the doctor
4. [ ] The live run over the maintainer's token (the main session's) in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/109-doctor-consent-and-release-pin` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the two scripted-token runs, the live doctor's consent row>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
