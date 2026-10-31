---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 005 — Group, Teams and SharePoint lookups: members and owners, groups without owners, team list, site list

## Context

The first read commands of the groups domain over the Graph client (task 003, same branch): `group members
<name or id>`, `group owners <name or id>`, `group owners-missing` (every group with no owner), `team list`,
`site list`; a name resolves through `$search` or `startsWith` with `ConsistencyLevel: eventual`, an ambiguous
name lists the candidates and exits 4 with a hint. Each command renders through the output contract.
Why: the daily questions the admin centers answer slowly, and the first real use of the client and the contract.
Done when: each command answers from fixtures in the tests and from the live tenant after a `login` (the live
check runs in the main session's signed-in round), and `group owners-missing` over the tenant finishes in well
under a second plus the network.
Bundled with 003 and 007 on one branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the delegated Graph permissions these lookups need
  (`Group.Read.All`, `Team.ReadBasic.All`, `Sites.Read.All` or what the research settles), one line each
- `README.md`: the lookups in the first-run section
- `tests/fixtures/README.md`: the new fixtures

## Not in this change

- any write (group membership and owners are M3, behind a plan)
- the window's group page (task 017)
- SharePoint sharing and permissions (M5)

## Steps

1. [x] the group resolution (id; exact name or address with `$filter`, else `$search`; `ConsistencyLevel: eventual`;
       no match and the ambiguous case as `NotFound`, the candidates in the hint)
2. [x] `group members`, `group owners`, `group owners-missing` (one paged `$filter=owners/$count eq 0`)
3. [x] `team list`, `site list`
4. [x] the result records, the table views, the fixtures, the snapshots and the child-process tests
5. [x] docs: the permissions, the README lines, the fixtures README
6. [x] the live check in the main session's signed-in round, `group owners-missing` timed

## Log

- 2026-10-29 — promoted by the main session into the Graph bundle (003, 005, 007)
- 2026-10-29 — verified against the Graph docs before coding: list groups (`$filter`, `$search` on `displayName`
  and `description` with `ConsistencyLevel`, page size up to 999;
  https://learn.microsoft.com/graph/api/group-list), the `owners` relationship supports `$filter` with
  `/$count eq 0` as an advanced query and `$expand` not with it
  (https://learn.microsoft.com/graph/aad-advanced-queries), list members and owners (any query parameter but
  `$expand` only as an advanced query; least privileged `GroupMember.Read.All` and `GroupMember.ReadBasic.All`, and
  a member of a type the token cannot read comes back with its id and type only, so `User.ReadBasic.All` adds the
  users' names; https://learn.microsoft.com/graph/api/group-list-members,
  https://learn.microsoft.com/graph/api/group-list-owners,
  https://learn.microsoft.com/graph/permissions-overview#limited-information-returned-for-inaccessible-member-objects),
  owners are not kept in Graph for groups created in Exchange, distribution groups and synced groups, list teams
  (`GET /teams`, `Team.ReadBasic.All`, only id, name, description and visibility filled;
  https://learn.microsoft.com/graph/api/teams-list), search sites (`GET /sites?search=`, delegated
  `Sites.Read.All`; `getAllSites` is application-only; https://learn.microsoft.com/graph/api/site-search,
  https://learn.microsoft.com/graph/api/site-getallsites)
- 2026-10-29 — built: `Groups/` in the Core (`GroupLookups`: the resolution, members, owners, the groups without an
  owner; `WorkspaceLookups`: teams, sites; the result records), `group`, `team` and `site` commands with their
  table views. Decisions: `group owners-missing` is one paged advanced query instead of `$batch` or `$expand` over
  every group, the fastest shape Graph offers; it leaves out (and counts) the distribution, mail-enabled security
  and synced groups, whose owners Graph does not keep, unless `--all-types`; the kind of a group comes from
  `groupTypes`, `securityEnabled` and whether it has an address, so every group request selects the same six
  fields; `site list` is the delegated search (`*` by default), trimmed to what the account can open. The made-up
  tenant's SharePoint hosts (`contoso.sharepoint.com`) are now allowed by `check-fixtures.py`. 172 tests green
## Deviations from the plan

- `group owners-missing` is one paged advanced query (`$filter=owners/$count eq 0`), not `$batch` or `$expand` over
  every group, and leaves out the groups Graph keeps no owners for unless `--all-types`.
- The delegated permissions are `GroupMember.Read.All` and `User.ReadBasic.All` (the members' names) instead of
  `Group.Read.All`; `Team.ReadBasic.All` and `Sites.Read.All` as planned.
- `group owners-missing` measured 7.9 s against the tenant, not "well under a second plus the network": three
  paged requests of an advanced query over Remote Desktop; a faster shape is a follow-up.

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end (0 warnings), `dotnet test` 173 tests green, `changelog.py --check`, `check-fixtures.py` (92 files, 0 findings); with an empty `M365AEGIS_HOME` the exe printed its version, `doctor --offline` exited 0, `whoami` exited 3; `release.ps1 -DryRun` green. The new fixtures were run through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — against the tenant (a tenant of a couple of hundred ownerless groups and a few hundred teams), in the main session's
  signed-in round on the maintainer's machine over Remote Desktop, after the consent of the five delegated
  permissions and a fresh device-code login (`whoami` listed them on the Graph token), stdout redirected:
  `group members "<a group>"` exit 0 in 0.7 s, 2 request ids, 1 member; `group owners "<the same group>"` exit 0
  in 0.8 s, 2 request ids, 0 owners; an ambiguous word shared by many group names exit 4 `not-found`, "names 10
  groups, and maybe more", the candidates with their ids in the hint; `--json group owners-missing` exit 0 in
  7.9 s (7889 ms elapsed), 3 request ids, a couple of hundred groups plus the left-out count: Graph accepted the `owners/$count
  eq 0` advanced query on groups, and the time is three paged requests over Remote Desktop, short of the "well
  under a second" of the plan (a follow-up); `team list` exit 0 in 1.5 s, 4 request ids, a few hundred rows; `site list`
  exit 0 in 2.0 s, 1 request id, a hundred-odd rows

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
