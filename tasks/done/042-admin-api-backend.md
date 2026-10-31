---
size: M
origin: done/010-spike-exchange-online-admin-api-in-the-tenant.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-30
milestone: M2 Exchange
---

# 042 — The Exchange Online Admin API backend: Send on Behalf, folder permissions, group membership, organization reads

## Context

The second layer of D-004 as a backend behind the same interface as the sidecar: the broker's token for
`https://outlook.office365.com/.default` (`Exchange.ManageV2`), POST calls with a `CmdletInput` body and
`X-AnchorMailbox: UPN:<admin>` to `adminapi/v2.0/<tenant id>/…`, `@odata.nextLink` paging through the Graph
client's shape; the reads the sidecar answers slowly or not at all: Send on Behalf with display names, mailbox
folder permissions, distribution group membership, the organization configuration; fixtures under
`tests/fixtures/graph/adminapi/`.
Why: task 010 proved on 2026-10-29 that the API answers in the tenant with the broker's token (`OrganizationConfig`
in 0.6 s, `Mailbox` paged); a REST call is one round trip where the sidecar costs a 2.4 s cold connect.
Done when: each of those reads answers from fixtures and live after one sign-in, a `mailbox` command picks the
backend by the read it needs, and `docs/development.md` names which read goes through which layer.
Part of the Exchange M2 bundle (041–044) of the round of 2026-10-29; the task session owns the steps below and
may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, "The Exchange Online sidecar": a section on the Admin API backend and a table of which
  read goes through which layer
- `README.md`: the new mailbox reads under the bundle's own heading
- `tests/fixtures/README.md`: the `adminapi/` fixtures
- `tasks/decisions/D-004-…`: a dated note that the second layer exists (append, never rewrite)

## Not in this change

- writes through the Admin API (M3)
- the undocumented `adminapi/beta/InvokeCommand` endpoint (never called, CLAUDE.md)
- the window's mailbox pages (task 045)

## Steps

1. [x] The backend behind the interface the sidecar implements: the token, the request shape, paging, the error
   mapping to the existing exit codes
2. [x] The reads: Send on Behalf with display names, mailbox folder permissions, distribution group membership,
   the organization configuration; the `mailbox` command picks the backend per read
3. [x] Fixtures under `tests/fixtures/graph/adminapi/`, the Core and Cli tests, the snapshots
4. [x] Live check (main session): each read after one sign-in, timings
5. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange M2 bundle (041–044)
- 2026-10-29 — checked against the reference before coding: every call `POST <base>/adminapi/v2.0/<tenant
  id>/<endpoint>` with `{"CmdletInput":{"CmdletName","Parameters"}}`, `X-AnchorMailbox` mandatory (for a mailbox
  call the target mailbox's key is recommended, the admin's otherwise; task 010 proved `UPN:<admin>` on `Mailbox`),
  `$select` on every endpoint, `ResultSize` (an integer or `Unlimited`, 1000 by default) and paging by posting the
  same body to `@odata.nextLink` (valid 5 to 10 minutes), unsupported cmdlets or parameters a 400; the endpoints:
  `Mailbox` (`Get-Mailbox`: `Identity`, `ResultSize`, `IncludeGrantSendOnBehalfToWithDisplayNames` in staged
  rollout; the response lists `GrantSendOnBehalfTo` as addresses and `GrantSendOnBehalfToWithDisplayNames`
  without documenting an entry's shape), `MailboxFolderPermission` (`Get-MailboxFolderPermission -Identity
  <mailbox>:\<folder>`, `ResultSize`; `User`, `AccessRights`, `SharingPermissionFlags`, `FolderName`),
  `DistributionGroupMember` (`Identity`, `ResultSize`; hidden members included), `OrganizationConfig` (no
  parameters; name, MailTips settings), `AcceptedDomain` (`Identity`, `ResultSize`)
  (https://learn.microsoft.com/exchange/reference/admin-api-get-started and the endpoint pages under
  https://learn.microsoft.com/exchange/reference/admin-api-endpoints-reference)
- 2026-10-29 — built: `Exchange/AdminApi/` (`AdminApiClient` behind `IExoCmdletRunner`, `AdminApiCmdlets` the five
  reads with their endpoints, `HttpAdminApiTransport` sending the Exchange Online token to
  `outlook.office365.com` only, `AdminApiConnection` with the fixtures under `M365AEGIS_GRAPH_FIXTURES` and none
  with `M365AEGIS_EXO_TEST_ORGANIZATION` alone); `ExchangeBackends` (the sidecar, the Admin API, Graph, more
  sidecars on demand) and `ExchangeRunner`'s overload over one sign-in, the envelope's `meta.backend` naming the
  layers used joined with `+`. Reads: `mailbox access <mailbox>` takes the mailbox and its delegates by name from
  the Admin API beside the sidecar's FullAccess and Send As (the sidecar alone when the Admin API cannot answer),
  `mailbox folder-permissions`, `mailbox group-members`, `mailbox organization`. Failures: 401 → 3, 403 → 8, 404
  or a 400 saying not found → 4, 429/503 after the retries → 5, other 400 → 13, 5xx or no network → 9; a cmdlet
  off the list → 12 before anything is sent. `probe admin-api --mailbox --folder --group` prints the shapes of the
  new reads, a list's first entry as its pattern. Fixtures under
  `tests/fixtures/graph/outlook.office365.com/adminapi/v2.0/<made-up tenant>/`, named by URL and the cmdlet's
  parameters
- 2026-10-30 — first live results (the main session's round): `mailbox folder-permissions <mailbox>` without
  `--folder` exited 4, because the folder path `Get-MailboxFolderPermission` takes is the folder's localized name
  (`Naptár` in this tenant; with `--folder Naptár` it answered 2 entries in 1.1 s). The cmdlet reference documents
  only `<mailbox>:\<folder path>` with example values, no language-free form
  (https://learn.microsoft.com/powershell/module/exchangepowershell/get-mailboxfolderpermission), while
  `Get-EXOMailboxFolderStatistics -FolderScope Calendar` returns the calendar folders with their `FolderPath` and
  `FolderType` (https://learn.microsoft.com/powershell/module/exchangepowershell/get-exomailboxfolderstatistics).
  Fixed: without `--folder` the sidecar names the folder whose type is `Calendar` first (the cmdlet joined the
  allow-list, both copies), the English `Calendar` is tried only when that call cannot answer, and a missing
  mailbox is exit code 4 before any folder is read; `--folder` reads through the Admin API alone. The fake module
  names Reception's calendar `Kalender`. The delegates by name came as text in the form `(<address>, <display
  name>)`, which the parser already read; the fixture now uses that form. While adding the cmdlet, a fake module
  that did not export it let pwsh auto-load the machine's real module for that one cmdlet in the tests (it failed
  unconnected; nothing reached a tenant); the fake's manifest now exports it

## Deviations from the plan

- The fixtures live under `tests/fixtures/graph/outlook.office365.com/adminapi/…` (the URL mirrored as the path,
  host first, as every Graph fixture), not `graph/adminapi/`: one folder and one variable answer both layers.
- The D-004 note is not appended here: a task session never edits a decision; its text is in the pull request's
  follow-ups for the main session to file.
- Exit code 12 (`cmdlet-not-allowed`) also covers a cmdlet off the Admin API backend's list: the same condition
  (a cmdlet refused, nothing sent) for the second layer; no new number.
- `mailbox folder-permissions` without `--folder` reads the calendar's localized name through the sidecar first
  (one `Get-EXOMailboxFolderStatistics`), so the default read is two layers, not the Admin API alone.
- `X-AnchorMailbox` is the administrator's `UPN:` for every call, the form task 010 proved, not the target
  mailbox's; the live round tells whether a mailbox read routed that way answers.

## Verification

- 2026-10-30 — offline, in the worktree, on the bundle's tip after the merge of `main` (9b6214b): the whole local
  matrix green (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and
  without the front end with 0 warnings, `dotnet test` 599 tests, `Invoke-Pester -Path tests/sidecar` 27,
  PSScriptAnalyzer 0 findings, `changelog.py --check`, `check-fixtures.py` 250 files and 0 findings, the commit
  rules' self-test; with an empty `M365AEGIS_HOME` `doctor --offline` exit 0 and `whoami` exit 3; the window's
  `--smoke` exit 0; `release.ps1 -DryRun` green); the new test data through the anonymizer scan, which flagged only
  the made-up contoso and example addresses and number-shaped quotas
- 2026-10-30 — live, in the main session's signed-in round (the worktree's Release build, the broker's cached token,
  which carries Exchange.Manage and Exchange.ManageV2): `probe admin-api --mailbox <a shared mailbox> --folder
  Naptár --group <a distribution group>` exit 0, every call 200 with the `UPN:` anchor (OrganizationConfig 420 ms,
  a `Mailbox` page 188 ms, the given mailbox 153 ms, its folder 551 ms with 2 entries, the group's members 138 ms
  with 2); the shapes: `GrantSendOnBehalfToWithDisplayNames` a list of text entries `(<address>, <display name>)`,
  `GrantSendOnBehalfTo` names, a folder entry's `User` an object `{@odata.type, DisplayName, Name,
  PrimarySmtpAddress}`. `mailbox access <that shared mailbox>` exit 0 in 4086 ms (5.5 s sidecar-only before),
  backend `admin-api+pwsh`, 15 holders (FullAccess 7, Send As 3, Send on Behalf 5, each of the 5 with its display
  name and address). `mailbox folder-permissions <mailbox> --folder Naptár` exit 0 in 1101 ms, backend `admin-api`,
  2 entries; `--folder NoSuchFolder` exit 4 in 1187 ms; without `--folder` exit 4 on the first build, fixed in this
  change (Log). `mailbox group-members <group>` exit 0 in 968 ms, backend
  `admin-api`, 2 members, 0 hidden. `mailbox organization` exit 0 in 782 ms, backend `admin-api`, the tenant's
  accepted domains. A mailbox that does not exist: `nobody@example.invalid` exit 4 `not-found`, backend `admin-api+pwsh`
  (`nobody@<the tenant's domain>` answered exit 0 with no grant: in this tenant that address is an alias of the
  Postmaster mailbox)
- 2026-10-30 — re-checked live on the fixed Release build (00:52–00:54, the cached token): `mailbox
  folder-permissions <mailbox>` without `--folder` exit 0 in 4892 ms, backend `admin-api+pwsh`, 2 request ids,
  `data.folder` = `Naptár` (the calendar's localized name, found by the sidecar), 2 entries; no sidecar left behind

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after #22)
- [x] moved to `done/` with `closed: 2026-10-30`; of the pull request's follow-ups the D-004 note is appended in
      the same commit, 048 (`mailbox list` through the Admin API, the report timed apart), 049 (one Exchange token
      source) and 050 (the fake module never falls back to the real one) are filed; the anchor by the target
      mailbox and the folder statistics for M5 were not filed (speculative, decided 2026-10-30)
