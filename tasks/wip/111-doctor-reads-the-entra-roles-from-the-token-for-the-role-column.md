---
size: M
origin: wip/109-doctor-per-permission-consent-check.md
created: 2026-11-01
started: 2026-11-02
plan: none
milestone: M6 Release and team
---

# 111 — The doctor reads the account's Entra roles for the permissions table's role column

## Context

The consent check of task 109 compares scopes only. A new `roles` row reads the account's Microsoft Entra directory
roles and names the rows of the setup guide's permissions table whose role the signed-in account lacks.
Why: a colleague with every consent but without the role learns the gap only when a command fails. The table's role
column is the one place where the tool states what role each command needs.
Decided 2026-11-02: the roles come from a Graph call, `GET /me/transitiveMemberOf/microsoft.graph.directoryRole`,
under the `User.Read` permission that is already consented. The backlog item proposed the `wids` claim of the Graph
token, and the reasons it was not used are: a client must not parse a token issued for another resource, `wids`
carries only roles assigned directly, and the call also sees the roles that come through a role-assignable group.
The Exchange Online role groups are not visible in Microsoft Entra. Exchange Administrator and Global Administrator
(Organization Management) and Global Reader (View-Only Organization Management) are counted for them; otherwise the
row is noted and never warned.
Done when: over a scripted role read the doctor names a missing role's rows and passes with the roles present; the role
template ids live next to the permission map, with a test that holds them to the table; and the live doctor over the
maintainer's token passes.

## Docs that move

- `docs/usage.md`: the doctor's check list gets the `roles` row
- `docs/development.md`: the doctor section, the role map and the test that holds it to the table
- `docs/setup.md`: one sentence under the permissions tables saying that the doctor reads the Entra roles but not
  the Exchange role groups

## Not in this change

- Any change to what the commands ask for (the scopes of task 002) or a new permission
- A role assignment started by the doctor (the fix line names the role, and an administrator assigns it)
- Reading the Exchange Online role groups (they would need a sidecar call)
- Eligible but inactive Privileged Identity Management roles (only the active ones count)

## Steps

1. [x] `Auth/RoleMap.cs`: the built-in roles the table names, with their template ids checked against the Entra
       built-in roles reference; one row per table row that names a role, with its kind (Entra, Entra or owner,
       Exchange)
2. [x] `RoleMapTests`: the guide's role column against the map, row by row
3. [x] The read: a `ReadDirectoryRoles` seam on `DoctorEnvironment`, the default through `GraphClient`
       (advanced query, `$select=roleTemplateId`); a failure skips the row
4. [x] The `roles` check in `Doctor.cs` after `consent`; `DoctorTests` over the seam; the doctor's snapshot tests
5. [x] Docs under *Docs that move*; the local matrix; the live doctor over the maintainer's token (silent sign-in)

## Log

- 2026-11-02 — promoted on `task/111-doctor-entra-roles` by the main session after the backlog review
- 2026-11-02 — the shape:
  - **The map**: `Auth/RoleMap.cs` holds the seven built-in roles the guide names. Their template ids were checked
    against the Entra built-in roles reference: Global Administrator, Exchange Administrator, Global Reader, Reports
    Reader, Security Reader, Groups Administrator and User Administrator.
  - **The rows**: the map has ten rows, one for each table row whose role column names a role. Each row lists the
    roles any one of which answers it, and Global Administrator answers every row.
  - **The kinds**:
    - *Entra*: the sign-in and audit lists, and the mailbox size over every mailbox.
    - *Entra or owner*: the four Graph group writes.
    - *Exchange*: everything that needs an Exchange Online role group. Exchange Administrator and Global
      Administrator carry Organization Management. Global Reader carries View-Only Organization Management, which is
      enough for the read rows but not for `trace`, which needs the Message Tracking role.
  - **The read**: `Auth/DirectoryRoles.cs` calls `/me/transitiveMemberOf/microsoft.graph.directoryRole` as an
    advanced query under `User.Read`. A role without its template id fails the read instead of looking like a missing
    role.
  - **The test helper**: the guide's table reader moved out of `PermissionMapTests` into `Auth/SetupGuide.cs`, which
    both map tests use.

## Deviations from the plan

- Step 3 said "null on a failure". Instead the seam throws, and the check catches the failure (`AegisException`,
  `BrokerException`) and skips the row with the reason, as the `sign-in` row does.

## Verification

- 2026-11-02 — offline:
  - `DoctorTests`:
    - Global Administrator → pass.
    - No role → warn, naming the four Entra rows (the group writes with "unless the account owns the group") and
      noting the six Exchange rows.
    - Global Reader → warn on the group writes only, with the Exchange write rows and `trace` noted.
    - Reports Reader + Groups Administrator + an unknown role → pass, "and 1 other role".
    - Two unknown roles → counted.
    - The row is skipped without a token, without a Graph token, on a refused read and when the broker fails.
    - The profile is passed to the read.
  - `DirectoryRolesTests`: the cast, the advanced query, a repeated role named once, a role without a template id
    failing the read, and the real machine's read asking for the profile first.
  - `RoleMapTests`: the guide's ten role rows equal the map. The Entra roles the guide names are accepted by their
    row. The Exchange and owner kinds follow the column.
  - The doctor's envelope and table snapshots carry the new row.
- 2026-11-02 — the local matrix in the worktree:
  - format, build and `dotnet test`: 1658 passed, 0 failed.
  - Coverage: Core 6874/6874 lines and 3513/3513 branches, Cli 2159/2159 lines and 550/550 branches.
  - `changelog.py --check`, `check-fixtures.py --self-test` and the scan: 530 files, 0 findings.
  - `doctor --offline`: exit 0, `roles` skipped.
  - Two timing tests of other tasks failed once each in a full run under the coverage collector and passed when
    rerun: the sidecar hang test, and `TheCalendarReadsRunEightAtOnceAndHalfAsManyAfterAThrottledOne` (passed three
    times out of three alone).
- 2026-11-02 — live, the main session, over the maintainer's cached token (silent sign-in, no `login`), with `doctor
  --table` from this branch's build, exit 0:
  - `roles` passes: "Global Administrator and 1 other role; every role of the setup guide's table". This confirms
    that `roleTemplateId` comes back under `User.Read`.
  - `consent` still warns about the two Graph write permissions of task 109's live run. Consenting to them in the
    portal is the maintainer's step.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
