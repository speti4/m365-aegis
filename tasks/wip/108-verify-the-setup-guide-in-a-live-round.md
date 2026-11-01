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

1. [x] `SidecarFailures`: a `cmdlet-failed` that names a cmdlet the session does not know ("is not recognized",
       `CommandNotFoundException`) is `forbidden` (8) with a hint that says why the cmdlet is missing; the mapping test's
       new rows, the Pester test pins the reply's error id, the docs under *Docs that move*
2. [x] The Azure CLI block of `docs/setup.md` run end to end against a throwaway registration (the maintainer's hand,
       the tool's harness refuses tenant writes), each step's result checked, the registration deleted; the doc
       corrected where the run differed
3. [x] The role probe with the everyday account: `trace` without a role, the hidden-cmdlet shape of
       `group owners-missing --all-types --verbose`, then `trace` in Recipient Management; the role taken out, the
       profile's account restored
4. [x] `docs/setup.md`: the `trace` row names the role; the `MessageTraceLookup` hint names the role next to the
       service principal if the live refusal is the same for both
5. [ ] The Verification dated; the local matrix with the sidecar Pester tests

## Log

- 2026-11-01 — promoted on `task/108-verify-the-setup-guide-in-a-live-round` by the main session
- 2026-11-01 — Core: `SidecarFailures` reads a `cmdlet-failed` that names a cmdlet the session does not know ("is not
  recognized", `CommandNotFoundException`) as `forbidden` (8) with a hint that says Exchange Online imports only the
  cmdlets the account's roles allow; a connect that says so stays 11. The mapping theory gained three rows and a test
  of the message and the hint; the Pester test of a cmdlet the fake module lacks pins the reply's error id
  (`CommandNotFoundException`), so the C# shape is the script's. `docs/development.md` (the exit codes) and
  `docs/usage.md` (the fallback paragraph) say so.
- 2026-11-01 — live, the Azure CLI block (the maintainer's hand: the session's harness refuses a tenant write), Azure
  CLI 2.71.0 signed in as the administrator: every line of the block ran as written against a throwaway registration
  `m365-aegis-setup-check` (create 3.1 s, update 2.0 s, sp create 1.5 s, the two permission adds 3.7 s and 3.3 s, the
  consent 3.0 s); `az ad app show` gave `isFallbackPublicClient` true, `AzureADMyOrg`, the two redirect URIs, 11 Graph
  and 2 Exchange scopes of type `Scope`; `az ad app permission list-grants` gave both grants with `AllPrincipals` and
  every scope named. The one thing the guide did not say: each `permission add` ends with a warning that a
  `permission grant` is needed, which the `admin-consent` line is; the guide says so now. The registration and its
  service principal were deleted (`az ad app delete`; both lists empty afterwards).
- 2026-11-01 — live, the everyday account (no Exchange Online role group), signed in once through the broker:
  `group owners-missing --all-types --verbose` exits 0 with backend `graph+pwsh`, the sidecar's `Get-DistributionGroup`
  answered (some 10 groups Exchange manages among some 2,100), so a plain mailbox user is not refused that read and
  the hidden-cmdlet shape did not show live; the mapping stays for a session that does hide a cmdlet (the fake module's
  test shows the shape). `mailbox list` exits 0 with backend `admin-api` and one mailbox, the account's own, with no
  warning: the Admin API scopes the list to what the account may see instead of refusing. `trace --recipient <own
  address> --days 2` exits 9: Microsoft Graph answered 500 to `/admin/exchange/tracing/messageTraces`,
  `InternalServerError: Failed parsing error response`, the same on eight runs. The account was then put into
  Recipient Management (15:01) and the trace still answered the 500 on six runs over the next eight minutes; the role
  was taken out again. Every Global Administrator of the tenant was notified of the privilege change, so no role is
  changed in the tenant from a session again (a memory note holds it); the probe is inconclusive, and the table's
  role cell is the Learn reference's (the Message Tracking role's role groups), with the 500 as the live shape of a
  missing role.
- 2026-11-01 — Core: the trace's `ProvisioningHint` names the role next to the service principal and points to
  `docs/setup.md`; a 500 whose message is "Failed parsing error response" keeps exit code 9 (the service did not say
  the account was refused) with a hint that names the role (`MessageTraceLookup.IsRoleShaped`, both lookups); tests
  for the shaped and a plain 500. `docs/setup.md`: the `trace` row's role cell, the troubleshooting line for the 500.

## Deviations from the plan

- The trace role is the Learn reference's, not a live-confirmed least role: the probe with the everyday account in
  Recipient Management did not answer within the round (Exchange RBAC propagation), and the privilege change notified
  every Global Administrator, so it is not repeated.
- The hidden-cmdlet shape did not show live (a plain mailbox user may run `Get-DistributionGroup`); the mapping is
  kept as the fake module's test shows the shape for a cmdlet the session lacks.
- The trace's 500 for an account without the role got a hint of its own (not planned): the live shape was a service
  error, not the 8 the plan expected.

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
