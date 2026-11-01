---
size: S
origin: wip/106-user-facing-readme-and-setup-guide.md
created: 2026-10-31
started: 2026-11-01
plan: none
milestone: M6 Release and team
---

# 108 — Verify the setup guide in a live round: the Azure CLI registration block and the least role for the message trace

## Context

In a signed-in round, run the Azure CLI block of `docs/setup.md` ("With the Azure CLI") against a throwaway
registration in the test tenant and fix whatever differs from the Learn reference it was written from; then find the
least Exchange Online role an account needs for `trace` (the guide's permissions table names none, because Microsoft's
Graph reference names none) and write it into the table.
Why: the guide is the administrator's path into the tool for every other tenant, and its two unverified places are
the ones a stranger cannot check against anything else.
Done when: the CLI block has run end to end once (the registration deleted afterwards), the table names the trace
role, and both carry the date of the round in the task's Log.
Also in this change, from the close of task 096: a session without an Exchange Online role has the cmdlet left out
(Exchange Online imports only the cmdlets the account's roles allow), so `Get-DistributionGroup` fails as "is not
recognized" (`CommandNotFoundException`), which `SidecarFailures` mapped to exit code 13 and task 096's fallback did not
catch; the shape is read as a refused role (exit code 8) now, so `group owners-missing --all-types` falls back to
Graph's list for it too.
Decided 2026-11-01: the role is probed with the maintainer's everyday account (no Exchange Online role) put into
Recipient Management for the round and taken out after it; the Learn permissions reference puts the Message Tracking
role in Recipient Management, Organization Management, Records Management and Compliance Management, and not in
View-Only Organization Management.
Fifteenth round of 2026-11-01, worked in the main session on its own branch (no worktree, no task session).

## Docs that move

- `docs/setup.md`: the `trace` row's role cell, the exit code 8 troubleshooting line, and whatever the live run of the
  Azure CLI block corrects in it
- `docs/development.md`, "The exit codes" of the sidecar: a hidden cmdlet is 8
- `docs/usage.md`, the `owners-missing --all-types` fallback paragraph: a hidden cmdlet falls back too

## Not in this change

- A doctor row for the account's Exchange Online roles or for the cmdlets a session lacks (`checkCommands`; backlog
  111 is about the Entra roles)
- The portal and the Graph PowerShell variants of the registration, checked against Learn in task 106

## Steps

1. [ ] `SidecarFailures`: a `cmdlet-failed` that names a cmdlet the session does not know ("is not recognized",
       `CommandNotFoundException`) is `forbidden` (8) with a hint that says why the cmdlet is missing; the mapping test's
       new rows, the Pester test pins the reply's error id, the docs under *Docs that move*
2. [ ] The Azure CLI block of `docs/setup.md` run end to end against a throwaway registration (the maintainer's hand,
       the tool's harness refuses tenant writes), each step's result checked, the registration deleted; the doc
       corrected where the run differed
3. [ ] The role probe with the everyday account: `trace` without a role, the hidden-cmdlet shape of
       `group owners-missing --all-types --verbose`, then `trace` in Recipient Management; the role taken out, the
       profile's account restored
4. [ ] `docs/setup.md`: the `trace` row names the role; the `MessageTraceLookup` hint names the role next to the
       service principal if the live refusal is the same for both
5. [ ] The Verification dated; the local matrix with the sidecar Pester tests

## Log

- 2026-11-01 — promoted on `task/108-verify-the-setup-guide-in-a-live-round` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
