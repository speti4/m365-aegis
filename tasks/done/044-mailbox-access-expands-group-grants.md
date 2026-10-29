---
size: M
origin: done/016-mailbox-access-reverse-lookup-and-forwarders.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-30
milestone: M2 Exchange
---

# 044 — `mailbox access` expands a grant made to a group into the group's members

## Context

When FullAccess, Send As or Send on Behalf is held by a group, `mailbox access <mailbox>` lists the group's
members (through `group members`, transitive) under the group's row, and `mailbox access --user <upn>` finds the
mailboxes a user reaches through a group; the expansion is a switch (`--expand-groups`) so the plain answer stays
one sidecar round.
Why: the lookups of task 016 list the group, not who is in it, and "who can open this mailbox" means the people.
Done when: a grant to a group answers with the members from the fake module and the Graph fixtures, and live on a
shared mailbox whose access is granted to a group.
Part of the Exchange M2 bundle (041–044) of the round of 2026-10-29; the task session owns the steps below and
may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, "The lookups": the switch, the Graph calls it adds and the row shape
- `README.md`: the switch under the bundle's own heading
- `tests/fixtures/README.md`: the fixtures the expansion needs

## Not in this change

- the window's mailbox page (task 045)
- writes to the grants (M3)

## Steps

1. [x] `--expand-groups` on `mailbox access <mailbox>`: a group holder's transitive members under the group's
   row (the `via` field of task 041 where it helps), the Graph calls batched
2. [x] `--expand-groups` on `mailbox access --user <upn>`: the user's transitive group memberships matched
   against the group holders
3. [x] Fake-module and fixture tests, the snapshots
4. [x] Live check (main session): a shared mailbox whose access is granted to a group
5. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange M2 bundle (041–044)
- 2026-10-29 — checked against the reference before coding: `/users/{id|upn}/transitiveMemberOf` lists a user's
  groups at every nesting level, cast to groups with `/microsoft.graph.group` (an advanced query:
  `ConsistencyLevel: eventual` and `$count`), `GroupMember.Read.All` among the permissions that read another user's
  memberships (https://learn.microsoft.com/graph/api/user-list-transitivememberof); a mailbox permission's `User`
  is any security principal, a group included
  (https://learn.microsoft.com/powershell/module/exchangepowershell/get-exomailboxpermission)
- 2026-10-29 — built: `mailbox access <mailbox> --expand-groups`: the holders resolved with one piped
  `Get-EXORecipient`, a recipient type with `Group` in it a group, whose object id's members come from Graph in two
  rounds of `$batch` whatever the number of groups (the direct and transitive members of every group, then the
  direct members of the groups nested in them; task 041's `via`); `MailboxHolder.Group` and `.Members`, a group's
  members under its row with a `through` column (`Finance Staff > Finance EU`). `mailbox access --user <upn>
  --expand-groups`: the user's transitive groups from Graph beside the sidecar's reads, matched against every
  entry's holder by display name, address, alias or object id, a holder no name matched (and no mailbox is)
  resolved with one piped `Get-EXORecipient`; `UserGrant.Via`, `UserAccess.Groups`. The fake module has the
  mail-enabled security group Finance Staff holding FullAccess on Reception; the Graph fixtures hold its members
  (Carol, and Dave and a device through Finance EU) and carol's and dave's groups

## Deviations from the plan

- A dynamic distribution group holding a grant is marked a group but not expanded: Graph holds no object for
  it (no `ExternalDirectoryObjectId`); the footer counts such groups.

## Verification

- 2026-10-30 — offline, in the worktree, on the bundle's tip after the merge of `main` (9b6214b): the whole local
  matrix green (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and
  without the front end with 0 warnings, `dotnet test` 599 tests, `Invoke-Pester -Path tests/sidecar` 27,
  PSScriptAnalyzer 0 findings, `changelog.py --check`, `check-fixtures.py` 250 files and 0 findings, the commit
  rules' self-test; with an empty `M365AEGIS_HOME` `doctor --offline` exit 0 and `whoami` exit 3; the window's
  `--smoke` exit 0; `release.ps1 -DryRun` green); the new test data through the anonymizer scan, which flagged only
  the made-up contoso and example addresses and number-shaped quotas
- 2026-10-30 — live, in the main session's round (the worktree's Release build, the broker's cached token): a scan
  of the 156 shared mailboxes with `--expand-groups` (724 s in all, about 4.6 s per cold process) found the grants
  held by groups. `mailbox access <a shared mailbox> --expand-groups`: exit 0 in 7888 ms, backend
  `admin-api+pwsh+graph`, 3 request ids, 23 holders, 2 of them groups (`group: true`), the first with 86 members,
  none nested (a flat group), member rows `{type, displayName, userPrincipalName, mail, id, nested: false}` without
  `via`, and the table's `through` column after `address`. `mailbox access --user <a member of that group>
  --expand-groups`: exit 0 in 79574 ms (the piped scan underneath, before the parallel default), backend
  `pwsh+graph`, 2 request ids, 5 grants, 2 of them with `via` = the group (`{id, displayName}`), `groups` 1

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after #22)
- [x] moved to `done/` with `closed: 2026-10-30`
