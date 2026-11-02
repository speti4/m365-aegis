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

1. [ ] `Auth/RoleMap.cs`: the built-in roles the table names, with their template ids checked against the Entra
       built-in roles reference; one row per table row that names a role, with its kind (Entra, Entra or owner,
       Exchange)
2. [ ] `RoleMapTests`: the guide's role column against the map, row by row
3. [ ] The read: a `ReadDirectoryRoles` seam on `DoctorEnvironment`, the default through `GraphClient`
       (advanced query, `$select=roleTemplateId`), null on a failure
4. [ ] The `roles` check in `Doctor.cs` after `consent`; `DoctorTests` over the seam; the doctor's snapshot tests
5. [ ] Docs under *Docs that move*; the local matrix; the live doctor over the maintainer's token (silent sign-in)

## Log

- 2026-11-02 — promoted on `task/111-doctor-entra-roles` by the main session after the backlog review

## Deviations from the plan

- none yet

## Verification

- 

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
