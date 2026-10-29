---
size: S
origin: done/005-group-teams-and-sharepoint-lookups.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 033 — `group members --transitive`

## Context

A `--transitive` switch on `group members` that reads `/groups/{id}/transitiveMembers` instead of
`/members`, so nested group membership answers in one call; the row shows the member's type and, where the
endpoint gives it, through which group it is a member.
Why: a group nested in a group is the usual shape of a department's access, and the direct list hides it.
Done when: the switch answers from fixtures and live, and the table marks nested members.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the switch
- `docs/development.md`, the group lookups: the endpoint and what the row shows
- `tests/fixtures/README.md`: the new fixture

## Not in this change

- a transitive owners view
- the window's group page (task 017)

## Steps

1. [x] The switch on `group members`, the `transitiveMembers` request, the member type in the row
2. [x] A fixture with a nested member, the Core and Cli tests, the snapshot
3. [x] Live check (main session): a group with a nested group
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference: list group transitive members (`GET /groups/{id}/transitiveMembers`,
  a flat list of every nested member as `directoryObject`s with their `@odata.type`; `$count`, `$filter`,
  `$search`, `$select`, `$top` only as advanced queries; 100 to a page by default, 999 at most; least privileged
  `GroupMember.ReadBasic.All`, so the `GroupMember.Read.All` the group lookups have covers it; a member of a type the
  token cannot read comes back with its id and type only; https://learn.microsoft.com/graph/api/group-list-transitivemembers)
- 2026-10-29 — built: `--transitive` on `group members`; the transitive list says nothing about the group a member
  came through, so the direct list goes out beside it (the two in parallel after the resolution) and a member
  missing from the direct list is marked nested; `GroupMember.Nested` (null in a direct list, so the direct envelope
  is unchanged) and `GroupMembers.Transitive`; the table adds a membership column and counts the direct and nested
  members and the nested groups under it. The fixture is in the transport's short form (its path under `graph/` is
  over 150 characters, task 036)

## Deviations from the plan

- The row does not name the group a nested member came through: the endpoint does not give it, and finding it
  costs a members request per nested group (a follow-up).

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token): `group members <id> --transitive` on a synced security group with 14 nested groups exit 0 in 0.8 s, 3 request ids (the resolution, the direct and the transitive list), 41 members (14 groups, 27 users), 28 of them marked nested; the table showed the membership column (`direct`, `nested`) and `-` for the groups' sign-in name; twelve Teams-backed groups tried before it had no nested member, as Microsoft 365 groups cannot nest

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
