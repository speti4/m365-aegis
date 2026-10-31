---
size: S
origin: done/061-distribution-group-members-through-exchange.md
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 064 — Owners of a distribution or mail-enabled security group through Exchange

## Context

`group owners add|remove` on a distribution group or a mail-enabled security group plans against the group's
`ManagedBy` (read through the Admin API's `Get-DistributionGroup`) and applies through the sidecar's
`Set-DistributionGroup -ManagedBy` (the allow-list and the fake module grow, with a D-004 note), the way task 061
routes the member writes; a synced group stays on Graph with its warning; a Microsoft 365 group still goes
through Graph.
Why: task 061 left the owners out by design; the support team's distribution lists change hands as often as
members.
Done when: the plan on a distribution group from the fake module shows the owner diff, `--yes` applies through
the sidecar, and a unified group still goes through Graph.
Part of the writes follow-ups bundle (064, 065, 066) of the sixth round of 2026-10-30; the task session owns the
steps below and may reshape them. The D-004 note (the new cmdlet on the allow-list and why) is written by the
main session on `main` at the close from the text the pull request description gives. The live line is a
`group owners add <distribution group> <user> --dry-run` on a cloud-only distribution group from the main
session; nothing is applied.

## Docs that move

- `docs/development.md`, "Writes and plans" and the Exchange layers: the owner writes' route per group type
- `README.md`: the bundle's own heading
- `tasks/decisions/D-004`: a note on `Set-DistributionGroup` (the main session, at the close)

## Not in this change

- Owner writes on a Microsoft 365 group (Graph, task 052) or a synced group (refused with the warning)
- `ManagedBy` of a dynamic distribution group

## Steps

1. [x] The read: `ManagedBy` through the sidecar's `Get-DistributionGroup` (the Admin API has no endpoint for it), the
       owners' names resolved with one piped `Get-EXORecipient`, matched the way 061 matches members
2. [x] The plan and the apply: `GroupWrites` routes an owner write on a distribution or mail-enabled security group
       to the sidecar's `Set-DistributionGroup -ManagedBy @{Add=…}` / `@{Remove=…}`; the allow-list (`ExoCmdlets`, the
       script), the fake module and the Pester tests grow; a synced group keeps the warning; a unified group stays on
       Graph
3. [x] Core and Cli tests over the fake module and the recorded answers
4. [x] Docs; the D-004 note text in the pull request description

## Log

- 2026-10-30 — promoted by the main session into the writes follow-ups bundle (064, 065, 066)
- 2026-10-30 — the Admin API's endpoint list checked (Microsoft Learn, "Exchange Online Admin API endpoints
  reference", read 2026-10-30): `AcceptedDomain`, `DistributionGroupMember`, `DynamicDistributionGroupMember`,
  `Mailbox`, `MailboxFolderPermission`, `OrganizationConfig`. No `Get-DistributionGroup`, so `ManagedBy` is read through
  the sidecar (Deviations). `Set-DistributionGroup -ManagedBy` takes the multi-valued change `@{Add=…}` / `@{Remove=…}`
  with `-BypassSecurityGroupManagerCheck` (Microsoft's reference and the troubleshooting article "You don't have
  sufficient permissions … distribution group", read 2026-10-30).
- 2026-10-30 — allow-list: `Get-DistributionGroup` (a read) and `Set-DistributionGroup` appended under a comment of
  their own in `ExoCmdlets.Allowed` and the script's list; `Set-DistributionGroup` with `Identity`, `ManagedBy`,
  `BypassSecurityGroupManagerCheck` and `Confirm` only in `ExoCmdlets.WriteParameters` and `$script:WriteParameters`.
  Both are REST cmdlets, so the connect's `-CommandName` imports them with the other writes. A first comment in the
  script's list had parentheses, which the allow-list test's pattern stops at; rewritten without.
- 2026-10-30 — `Groups/GroupWrites.cs`: `ThroughExchange(group)` now covers both lists of a distribution or mail-enabled
  security group that is not synced. The owners: `Get-DistributionGroup -Identity <address>` with `ManagedBy`, the
  names piped into `Get-EXORecipient` (the names, addresses, object ids, type), a name no recipient answers to (a role
  group such as Organization Management) kept as it is; a user matched by every name Exchange gives the recipient. Each
  change is one `Set-DistributionGroup -Identity <group address> -ManagedBy @{Add|Remove=<address>}
  -BypassSecurityGroupManagerCheck -Confirm:$false` through the sidecar. The Graph-only warning ("Graph keeps no
  owners") is not given when the owners go through Exchange; the last-owner warning counts `ManagedBy`.
- 2026-10-30 — fake module: the two groups carry `ManagedBy` by name (Sales: alice; Finance Staff: carol and the role
  group Organization Management); `Get-DistributionGroup` and `Set-DistributionGroup` added and exported; removing an
  owner who is not one, and an unknown recipient, fail as non-terminating errors; each change recorded. Graph fixture:
  the synced distribution group Sales Synced (`…03a`) and its empty owner list.
- 2026-10-30 — tests: `DistributionGroupWritesTests` (Sales: Dave planned, Alice unchanged by her `ManagedBy` name, no
  warning, backend `graph+pwsh`, the apply's `Set-DistributionGroup` with `ManagedBy {"Add":["dave@contoso.com"]}`;
  Finance Staff: Carol and the role group planned for removal, the last-owner warning, Carol applied and the role group
  refused, exit 6; Finance's owners still a Graph `$batch` with backend `graph`; Sales Synced on Graph with the synced
  warning; the parameter allow-list and the Admin API refusing `Get-DistributionGroup`), Pester (the read and the
  piped resolution, an owner added and another removed and read back, a removal of a non-owner and an unknown
  recipient, a setting off the list refused with nothing recorded), the Cli's
  `ADistributionGroupsOwnersArePlannedAndAppliedThroughTheSidecar` (JSON and table dry runs, the apply's record).
- 2026-10-30 — docs: `docs/development.md` (the group writes' bullet corrected; the bundle's own heading "The writes
  follow-ups: owners, resume and prune" with the owners' layer table and the allow-list), `README.md` (the bundle's
  heading), `tests/sidecar/README.md`, `tests/fixtures/README.md`.

## Deviations from the plan

- The current owners are read through the sidecar, not the Admin API: the Admin API's six documented endpoints have
  no `Get-DistributionGroup` (D-004 keeps the backend to its documented endpoints), so the read is
  `Get-DistributionGroup` on the sidecar's allow-list, and the plan's backend is `graph+pwsh` (a member write's is
  `graph+admin-api`). A dry run on a distribution group therefore starts a sidecar.
- `ManagedBy` names each owner by the recipient's name, not by an address, so the plan resolves the names with one piped
  `Get-EXORecipient` (already on the list); a name nothing resolves (a role group) is listed and can be removed by that
  name.
- `-BypassSecurityGroupManagerCheck` is always sent, as for the members (task 061).

## Verification

- 2026-10-30 — the local matrix in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  the Release build, `dotnet test` (833 tests, 0 failed), `changelog.py --check`, `check-fixtures.py` (286 files, 0
  findings), `Invoke-Pester -Path tests/sidecar` (50 passed, the 5 new owner cases among them), `doctor --offline` exit
  0, `whoami` exit 3 on an empty home.
- 2026-10-30 — against the fixtures and the fake module through the exe: `group owners add Sales dave@contoso.com
  alice@contoso.com --dry-run` exit 0, 1 change, 1 unchanged, 0 warnings, backend `graph+pwsh`; the `--table` form
  lists the change; `--yes` exit 0 with the fake's record of `Set-DistributionGroup -ManagedBy @{Add=dave@contoso.com}`.
- 2026-10-30 — live, the main session's signed-in round (this branch's build, dry runs only, nothing applied; the
  tenant has a hundred-odd distribution groups, most of them synced, a dozen mail-enabled security groups and a few
  hundred Microsoft 365 groups): `group owners add <cloud-only distribution group> <a non-owner> --dry-run --json` exit 0, 4988 ms wall
  (4868 ms in the envelope, a sidecar started), backend `graph+pwsh`, mode `dry-run`, 1 change, 0 unchanged, 0
  warnings, 3 request ids, each change with `index` and `key`; the `--table` form exit 0 in 3939 ms, one row `not an
  owner` → `an owner`, `planned`; `group owners add <synced distribution group> <user> --dry-run` exit 0 in 1102 ms,
  backend `graph`, 1 change, the synced-group warning, no sidecar; `group owners add <Microsoft 365 group> <a
  non-owner> --dry-run` exit 0 in 989 ms, backend `graph`, 1 change, 0 warnings. A current owner was not passed live
  (its name is not readable through Graph), so the "an owner already" row and the resolution of `ManagedBy` names are
  verified by the Core, Cli and Pester tests over the fake module only.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (the D-004 note's text is in the pull request; the main
      session writes it on `main`)
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
