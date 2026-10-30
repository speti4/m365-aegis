---
size: S
origin: done/052-group-membership-and-owner-writes.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 061 — Members of a distribution or mail-enabled security group through Exchange

## Context

`group members add|remove` on a distribution group or a mail-enabled security group runs
`Add-DistributionGroupMember` / `Remove-DistributionGroupMember` through the sidecar (the allow-list and the fake
module grow, with a D-004 note), since Graph refuses the `$ref` write on those group types; the plan reads the
current members through the Admin API's `Get-DistributionGroupMember` (task 042).
Why: task 052 warns and refuses on these groups; the support team's distribution lists are the common case.
Done when: the plan on a distribution group from the fake module shows the diff, `--yes` applies through the
sidecar, and a unified group still goes through Graph.
Part of the writes and plan records bundle (058, 061, 060, 062) of the round of 2026-10-30; the task session owns
the steps below and may reshape them. The live line is a `--dry-run` on a distribution group and on a unified group
from the main session's signed-in round; nothing is applied. The parallel Exchange and window bundle (055, 059, 056,
057) adds `-AutoMapping` to the allow-list and the fake module under its own comment: this task appends its cmdlets
under a comment of its own, so the two merge without a conflict.

## Docs that move

- `docs/development.md`, "The Exchange Online sidecar": the two cmdlets on the allow-list, the layer table
- `docs/development.md`, "Writes and plans": the group types and their layer
- `tasks/decisions/D-004-…`: a dated note on the write cmdlets added (the write layer grows by two)
- `README.md`: the bundle's own heading

## Not in this change

- owners of a distribution group (`ManagedBy`, a follow-up if wanted)
- dynamic distribution groups (no member write exists)

## Steps

1. [x] The group type told at plan time (unified → Graph; distribution and mail-enabled security → Exchange); the
   plan's current members from the Admin API's `Get-DistributionGroupMember`
2. [x] `Add-DistributionGroupMember` and `Remove-DistributionGroupMember` on the allow-list (`ExoCmdlets`, the sidecar
   script) with their parameter allow-list, and in the fake module
3. [x] Core and Cli tests (the diff on a distribution group, the apply through the fake module, a unified group still
   through Graph, the refusal gone); Pester for the fake module's new cmdlets
4. [x] Docs and the D-004 note

## Log

- 2026-10-30 — promoted by the main session into the writes and plan records bundle (058, 061, 060, 062)
- 2026-10-30 — allow-list: `Add-DistributionGroupMember` and `Remove-DistributionGroupMember` appended under a comment
  of their own in `ExoCmdlets.Allowed`, `ExoCmdlets.WriteParameters` and the script's two lists, each with `Identity`,
  `Member`, `BypassSecurityGroupManagerCheck` and `Confirm` (the parameters checked against Microsoft's reference
  "Add-DistributionGroupMember", read 2026-10-30); the connect's `-CommandName` imports them with the other writes; the
  script's comments carry no parentheses, which the allow-list test's pattern stops at. D-004 note of 2026-10-30.
- 2026-10-30 — `Groups/GroupWrites.cs`: `PlanAsync(ExchangeBackends, …)` beside the Graph one; a group's members go
  through Exchange when the group is a distribution or mail-enabled security group and not synced: the current members
  from the Admin API's `Get-DistributionGroupMember -Identity <the group's address> -ResultSize Unlimited` (Graph's
  list when the run has no Admin API or it cannot answer for a reason other than the sign-in), a user matched by every
  name Exchange gives the member (the addresses of `PrimarySmtpAddress` and `EmailAddresses`, the object ids, alias,
  name), and each change one `Add-`/`Remove-DistributionGroupMember -Identity <group address> -Member <member address>
  -BypassSecurityGroupManagerCheck -Confirm:$false` through the sidecar (the sidecar asked for in the step, so a dry
  run starts none). The Graph-only overload keeps the old warning. The Cli's group writes run over
  `WriteRunner.ExchangeAsync`, so the backend names the layers used: `graph` for a Microsoft 365 group,
  `graph+admin-api` for a distribution group's plan, `graph+admin-api+pwsh` after its apply.
- 2026-10-30 — fixtures: Sales (`…036`, a distribution group) by its exact name and Finance Staff (`…035`) by its id on
  the Graph side; `DistributionGroupMember` for `sales@contoso.com` (Bob, the contact Dave Private, Alice hidden) and
  `finance-staff@contoso.com` (Carol) on the Admin API's, request ids `…038` and `…039`. The fake module keeps the two
  groups by their members' addresses and records each member write; adding a member, removing a non-member and an
  unknown recipient fail as the service's do.
- 2026-10-30 — tests: `DistributionGroupWritesTests` (the plan of Sales from the Admin API with Bob and the hidden
  Alice unchanged and no warning, the apply through the fake's `Add-DistributionGroupMember` with the group's and the
  member's addresses and the switch; Finance Staff's removal through `Remove-DistributionGroupMember`; Graph's list
  without the Admin API; Eve refused by Exchange while Dave is applied, exit 6; Finance still a Graph `$batch` with
  backend `graph` and no Exchange write; the Graph-only plan's warning; the parameter allow-list), Pester (a member
  added and removed and added again, recorded without `Confirm`; a member already there, one who is not, an unknown
  recipient and a throttled identity; a parameter off the list refused with nothing recorded), the Cli's
  `ADistributionGroupsMembersArePlannedAndAppliedThroughExchange` (the dry run's backend, the apply's record of
  `Add-DistributionGroupMember`). Bundle B's test that the fake exports exactly the allow-list passes with the two.
- 2026-10-30 — docs: `docs/development.md` ("The member writes of a distribution group" in the sidecar section, two
  rows of the layer table, the group writes' bullet), the D-004 note, `README.md` (the bundle's heading),
  `tests/sidecar/README.md` and `tests/fixtures/README.md`.
- 2026-10-30 — a group synced from on-premises Active Directory stays on Graph with task 052's warning ("is synced from
  on-premises Active Directory: … the service will refuse the change"), by design: Exchange Online refuses a member
  write on a synced distribution or mail-enabled security group too, so neither layer can apply it and the change
  belongs on-premises. The main session's first live pick hit two such groups (the tenant has 135 distribution groups,
  15 cloud-only, and 6 mail-enabled security groups, 2 cloud-only) and saw exactly this; the docs say so in the group
  writes' bullet.

- 2026-10-30 — closed by the main session on `main` after #26; the follow-up (distribution group owners through Exchange) is filed as backlog item 064

## Deviations from the plan

- The plan falls back to Graph's member list when the run has no Admin API or it cannot answer (a refused permission,
  not enabled), as the mailbox lookups fall back to the sidecar; Graph reads a distribution group's members, it only
  refuses to change them.
- `-BypassSecurityGroupManagerCheck` is on the parameter list and always sent: without it an administrator who is not
  among the group's owners (`ManagedBy`) is refused, and the maintainer's account is a Global Administrator
  (Organization Management). An account without that role would be refused by the service, a `failed` change.
- The group is named to Exchange by its primary address, and a member by its address (else its object id), the forms
  every Exchange identity parameter documents.

## Verification

- 2026-10-30 — the local matrix in the worktree, as in task 058's Verification (758 .NET tests, Pester 42 with the
  three new cases, PSScriptAnalyzer 0 findings, the `windows` job's steps).
- 2026-10-30 — against the fixtures and the fake module: `group members add Sales dave@contoso.com bob@contoso.com
  --dry-run` exit 0, 1 change, 1 unchanged, no warning, backend `graph+admin-api`; `group members add Sales
  dave@contoso.com --yes` exit 0, backend `graph+admin-api+pwsh`, the fake's record of `Add-DistributionGroupMember
  -Identity sales@contoso.com -Member dave@contoso.com`; `group members add Finance dave@contoso.com` stays a Graph
  `$batch` with backend `graph`.
- 2026-10-30 — live, the main session's signed-in round (the broker's cached token), dry runs only: `group members add
  <cloud-only distribution group> <non-member> <member> --dry-run` exit 0, backend `graph+admin-api`, 1 change, 1
  unchanged `a member already` (the member matched; the tenant's user principal names equal the primary addresses), 0
  warnings, 3 request ids, 1240 ms in the envelope (1349 ms wall), no pwsh child started; the same as a table, two rows
  (`planned`, `unchanged`), 965 ms wall; `remove <distribution group> <member> <non-member> --dry-run` exit 0,
  `graph+admin-api`, 1 change, 1 unchanged `not a member`, 2 request ids, 812 ms; `add <cloud-only mail-enabled security
  group> <non-member> --dry-run` exit 0, `graph+admin-api`, 1 change, 3 request ids, 915 ms. On two synced groups the
  plan stayed on Graph (backend `graph`) with the synced-group warning, as designed. Nothing was applied.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
