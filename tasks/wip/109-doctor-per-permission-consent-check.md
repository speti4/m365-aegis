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

1. [x] The shape: the command-to-permission map in the Core (Graph scopes and the Exchange roles, as `docs/setup.md`'s
       table lists them), a test that reads the table and holds the map to it; the step's design in the Log
2. [x] The check in `Doctor.cs`: the permissions of each cached token against the map, a warning that names the
       missing table rows with the consent as the fix line, a pass when every one is consented, a plain line when no
       token is cached (today's `sign-in` row stays)
3. [x] The offline tests over scripted tokens: a missing permission warns with the row's name, a full set passes; the
       output contract's snapshot tests of the doctor
4. [x] The live run over the maintainer's token (the main session's) in the Verification; docs under *Docs that move*;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/109-doctor-consent-and-release-pin` by the main session
- 2026-10-31 — the design (step 1), decided by the task session:
  - **The map**: `Auth/PermissionMap.cs` holds one `PermissionRow` per row of `docs/setup.md`'s permissions tables
    that names a delegated permission (15 rows): the section (the `###` heading), the first column as the guide prints
    it without the backticks, and the permissions, each a `NeededScope` with its API (`Resource.Graph` or
    `Resource.Exchange`). Rows that name no permission (`call`, the snapshot reads) are left out.
  - **The test that holds it**: `PermissionMapTests` reads the tables under `## Permissions` (section, first cell,
    the backticked dotted names of the second cell, so `--all-types` is not a permission) and compares them with the
    map row by row in the guide's order; the API of each permission is held by the guide's Azure CLI lists
    (`Add-DelegatedPermission $graph …` and `$exchange …` equal the map's permissions per resource). The guide needed
    no new column or anchor: its text stays the administrator's.
  - **The check**: a `consent` row after `sign-in`, which hands its `SignInInfo` on (no second sign-in). Each
    acquired token's scopes (as `whoami` lists them: no resource prefix, compared without case) against the map: a
    pass names every permission per API; a warning names each table row that misses a permission with the ones it
    misses, and the fix line names the missing permissions per API to consent to, then `login`; a resource without a
    token is named as unread (the `sign-in` row has the reason and the fix) and its rows are not weighed; skipped
    offline and when no token was read. Not required.
  - **Roles are not checked**: the Context asks for what the tokens carry; no token names the account's Exchange
    Online roles (RBAC lives in Exchange), and the Entra roles would need another claim or call. The consent of the
    two Exchange scopes is checked like the Graph ones.
  - **No implied permission**: `GroupMember.ReadWrite.All` is not taken for `GroupMember.Read.All`; the table is the
    one list, and its Azure CLI block consents to both.

## Deviations from the plan

- Step 1 named "the Exchange roles": the map holds the two Exchange Online delegated permissions (`Exchange.Manage`,
  `Exchange.ManageV2`), not the roles of the table's last column, which no token carries (the design above).
- `docs/setup.md` is unchanged: the test reads the tables as they are, so the guide needed neither a column nor an
  anchor.
- `M365Aegis.Cli/Commands/DoctorCommand.cs`: the command's description and `--offline`'s name the consent too.

## Verification

- 2026-10-31 — offline, over scripted tokens (`DoctorTests`): every permission of the table consented → `consent`
  pass naming the 11 Microsoft Graph and 2 Exchange Online permissions; `AuditLog.Read.All` left out → warn
  `not consented: signin list, audit list (AuditLog.Read.All)`, fix `an administrator consents to AuditLog.Read.All
  (Microsoft Graph) in the portal (the setup guide's Permissions table), then m365-aegis login`; `Exchange.ManageV2`
  left out → the four Exchange rows that need it named; no Exchange Online token → named as unread, the Graph rows
  still checked; no token → skipped; the whole doctor over `AuthService` and `MarkedTokenBroker` (the scopes with
  their resource prefix, stripped as `whoami` shows them) → warn with the missing rows. `PermissionMapTests`: the 15
  rows of the guide's tables equal the map, and the Azure CLI lists equal its permissions per API.
- 2026-10-31 — the local matrix in the worktree: restore `--locked-mode`, format, the full build, `dotnet test`
  (1545 passed, 0 failed), coverage 100% lines and branches of the Core and the Cli, `changelog.py --check`,
  `check-fixtures.py --self-test` and the scan (0 findings), `doctor --offline` exit 0 (`consent` skipped, offline
  run), `whoami` exit 3 on an empty home, the window's `--smoke` exit 0; the doctor's snapshots (envelope and table)
  carry the new row.
- 2026-10-31 — live, the main session over the maintainer's token (silent sign-in, no login), `doctor --table` of
  this branch's build, exit 0: `sign-in` pass with both tokens; `consent` warn naming two rows, `group members add,
  group members remove (Microsoft 365 and security groups) (GroupMember.ReadWrite.All)` and `group owners add, group
  owners remove (Microsoft 365 and security groups) (Group.ReadWrite.All)`, with the fix line to consent to the two
  Microsoft Graph permissions, then `login`. `whoami --json` over the same token lists exactly the other permissions,
  so the row matches it; the sign-in asks `.default`, so a scope missing from the token is a consent missing on the
  registration: a real finding, the maintainer's to consent in the portal.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (`docs/setup.md` not needed: its tables are read as they are)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
