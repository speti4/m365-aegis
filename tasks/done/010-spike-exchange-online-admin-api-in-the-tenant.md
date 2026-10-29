---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 010 — Spike: the Exchange Online Admin API v2.0 in the tenant, `Exchange.ManageV2` for a public client

## Context

A timeboxed try (half a day): is the preview Admin API enabled for the tenant ("not available in all
organizations"), can a public client acquire `Exchange.ManageV2`, and does one call
(`adminapi/v2.0/<tenant id>/OrganizationConfig` with `X-AnchorMailbox`) answer with the broker's token; the
shape and the `nextLink` expiry noted for the backend of D-004.
Why: the second layer of the Exchange reach exists only if the tenant and the token allow it; otherwise the
sidecar carries those six endpoints too.
Done when: a dated note in this task says yes or no with the response shape, and the M2 backlog is adjusted.
Last of the Exchange bundle (009, 015, 016, 011, 010) of the round of 2026-10-29, timeboxed: the task session
writes a probe (a hidden command or a script, nothing tenant-specific committed), the main session runs it
live and sends the answer back; the M2 adjustment is a proposed follow-up in the pull request.

## Docs that move

- `tasks/decisions/D-004-…`: a dated note on the second layer (proposed by the session, filed by the main session)

## Not in this change

- an Admin API backend (a later task if the answer is yes)
- the undocumented `adminapi/beta/InvokeCommand` endpoint, never called

## Steps

1. [x] The probe: a token for `Exchange.ManageV2` from the broker, one call to `OrganizationConfig` with
       `X-AnchorMailbox`, the response shape and the `nextLink` expiry printed scrubbed
2. [x] Live run (main session): yes or no, the response shape
3. [x] The dated note here, the proposed D-004 note and the M2 follow-up in the final report

## Log

- 2026-10-29 — promoted by the main session into the Exchange bundle (009, 015, 016, 011, 010)
- 2026-10-29 — checked against the documentation before coding: every Admin API v2.0 call is a `POST
  <base>/adminapi/v2.0/<tenant id>/<endpoint>` with a `{"CmdletInput":{"CmdletName":…}}` body, `X-AnchorMailbox`
  mandatory (`UPN:<admin>` for a call without a target mailbox in the delegated flow; the table also names
  `AAD-UPN:`), delegated `Exchange.ManageV2` on the `https://outlook.office365.com/.default` resource, the RBAC
  role View-Only Organization Management for `OrganizationConfig`, `@odata.nextLink` valid 5 to 10 minutes
  (https://learn.microsoft.com/exchange/reference/admin-api-get-started,
  https://learn.microsoft.com/exchange/reference/admin-api-authentication,
  https://learn.microsoft.com/exchange/reference/admin-api-endpoints-organization-config). The probe is the hidden
  `m365-aegis probe admin-api`: the broker's Exchange Online token, `OrganizationConfig` with `UPN:` (and
  `AAD-UPN:` when refused), then one page of `Mailbox` (`ResultSize` 1) for the `@odata.nextLink`; it prints the
  statuses, the times, the service's error code and message, the token's permissions, and the shape of each
  answer (property names and JSON types, never a value) and the query parameter names of the next link
- 2026-10-29 — the note: yes. The Admin API v2.0 is enabled in the tenant and answers a public client's
  delegated broker token (the same `outlook.office365.com` token the sidecar uses, carrying `Exchange.ManageV2`):
  `OrganizationConfig` 200 in 637 ms with `X-AnchorMailbox: UPN:<admin>` (the `AAD-UPN:` retry was not needed),
  `Mailbox` with `ResultSize` 1 200 in 158 ms. Shapes: `OrganizationConfig` is `@odata.context`,
  `@adminapi.warnings` (a list) and `value[0]` with `Name`, `Identity`, `Guid` (each with an `@odata.type` twin),
  `MailTipsAllTipsEnabled`, `MailTipsExternalRecipientsTipsEnabled`, `MailTipsLargeAudienceThreshold`, `Id`;
  `Mailbox` is `@odata.context`, `@odata.nextLink` (a `$skiptoken` parameter), `@adminapi.warnings` (one entry)
  and `value[0]` with `UserPrincipalName`, `Alias`, `DisplayName`, `EmailAddresses`, `GrantSendOnBehalfTo`,
  `GrantSendOnBehalfToWithDisplayNames`, `ExternalDirectoryObjectId`, `HiddenFromAddressListsEnabled`,
  `MaxSendSize`, `PrimarySmtpAddress`, `RecipientType`, `RecipientTypeDetails`, `Identity`, `Id`, `Name`. The
  expiry of the next link was not observed (one page only; the documentation says 5 to 10 minutes). For D-004 the
  second layer stands: Send on Behalf (with display names, which the sidecar resolves in an extra call), folder
  permissions, distribution group membership and the organization reads can move to an Admin API backend; the
  M2 follow-up is that backend

## Deviations from the plan

- The call is a POST with a `CmdletInput` body, as the documentation names it, not a GET.
- A second call (`Mailbox`, one item) shows the `@odata.nextLink`, which `OrganizationConfig` never has.

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the
  Release build with the front end (0 warnings), `dotnet test` 388 green (the Core and Cli tests start the sidecar
  as a real pwsh child against the fake module), `Invoke-Pester -Path tests/sidecar` 20 green, PSScriptAnalyzer
  over the three folders CI checks 0 findings, `changelog.py --check`, `check-fixtures.py` (136 files, 0
  findings); with an empty `M365AEGIS_HOME` `doctor --offline` exited 0 (`sidecar-script` passes on the changed
  script), `whoami` 3, `mailbox list` 3; the window's `--smoke` 0; `release.ps1 -DryRun` green. The new test data
  went through the anonymizer scan, which flagged only the made-up contoso, fabrikam and example addresses
- 2026-10-29 — against the tenant, in the main session's signed-in round (evening, UTC+1) from this branch's
  Release build: `probe admin-api` exit 0, "the Admin API answered OrganizationConfig with the broker's token",
  the statuses, times and shapes of the note above; nothing tenant-specific is in the repo

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
