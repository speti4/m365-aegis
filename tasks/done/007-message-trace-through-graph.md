---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 007 — Message trace through Graph v1.0

## Context

`trace --sender <address> | --recipient <address> [--days N]` over `/admin/exchange/tracing/messageTraces` (the
10-day window, 90 days back, 100 requests per 5 minutes); the `ExchangeMessageTrace.Read.All` scope and the
one-time provisioning of the service principal documented in `docs/development.md`; the throttling budget
respected by the client (task 003, same branch).
Why: the first mailbox-domain question that Graph answers, so it needs no sidecar; the research found it GA in
v1.0 in 2026-02.
Done when: a trace of a made-up message answers from fixtures, a live trace of a known message answers after the
provisioning step (the scope consent and the provisioning are the maintainer's portal steps; the live check runs
in the main session's signed-in round once they are done, and may land after this pull request), and a window
wider than 10 days is refused with a hint instead of an empty answer.
Bundled with 003 and 005 on one branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the `ExchangeMessageTrace.Read.All` line and the
  one-time provisioning step, without real values
- `README.md`: `trace` in the first-run section
- `tests/fixtures/README.md`: the trace fixtures

## Not in this change

- the sidecar's `Get-MessageTrace` path (task 015/016)
- message trace details (`messageTraceDetails`) beyond the list, unless the endpoint makes it one call

## Steps

1. [x] the endpoint's shape verified against the current Graph v1.0 reference (the filter syntax, the window
       rules, the throttling limits); the request through the client
2. [x] `Commands/TraceCommand.cs`: the options, the 10-day rule as a usage error with a hint, the table view
3. [x] fixtures, snapshots, tests
4. [x] docs: the scope, the provisioning step, the README line
5. [x] the live check after the maintainer's consent and provisioning (the refusal checked; the answer
       lands after the pull request, once the service principal is provisioned)

## Log

- 2026-10-29 — promoted by the main session into the Graph bundle (003, 005, 007)
- 2026-10-29 — verified against the docs before coding: `GET /admin/exchange/tracing/messageTraces` in v1.0,
  delegated `ExchangeMessageTrace.Read.All`, `$filter` on `receivedDateTime` (`ge`/`le`, both bounds, at most 10
  days, the start at most 90 days back, the end not after now, `YYYY-MM-DDThh:mm:ssZ`), `senderAddress` and
  `recipientAddress` with `eq`, `$top` 1 to 5000 (default 1000), `@odata.nextLink` paging, 100 requests per 5
  minutes per tenant (https://learn.microsoft.com/graph/api/messagetracingroot-list-messagetraces,
  https://learn.microsoft.com/graph/api/resources/exchangemessagetrace); the service principal of the Microsoft
  application `8bd644d1-64a1-4d4b-ae52-2e0cbf64e373` is provisioned once per tenant, can take hours, and until then
  the API answers 401 "Service principal-less authentication failed"
  (https://learn.microsoft.com/exchange/monitoring/trace-an-email-message/graph-api-message-trace)
- 2026-10-29 — built: `Exchange/MessageTraceLookup.cs` in the Core and `trace` in the Cli. Decisions: `--days`
  (default 2, the service's 48 hours) and `--until` (default now) make the window, checked before anything is
  sent: wider than 10 days, starting before 90 days back or ending in the future is a usage error with a hint, so
  an empty answer always means no message; pages of 5000 to spend as few of the 100 requests as possible; a 403,
  or the 401 that names the missing service principal, is exit code 8 with the consent and provisioning step as
  the hint (a 401 is otherwise a sign-in failure, which this is not); the details per recipient
  (`getDetailsByRecipient`) stay out. The fixture names of a trace window are longer than git for Windows takes
  under a worktree, so the fixture transport gained a short form of a long name (tried second; the full name still
  works): the first 60 characters of the query and a hash. 172 tests green
## Deviations from the plan

- `--until` joins `--days`, so a window older than the last 10 days is reachable (up to 90 days back).
- A 401 that names the missing service principal is exit code 8 with the setup step as the hint, not exit 3: it is
  a setup step, not a sign-in to repeat.
- A trace window's fixture name is too long for git under a worktree, so the fixture transport finds long names
  in a short, hashed form (task 003's convention, extended).
- The live trace of a known message is not done yet: the service principal is not provisioned in the tenant; the
  refusal before it was checked live (below).

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end (0 warnings), `dotnet test` 173 tests green, `changelog.py --check`, `check-fixtures.py` (92 files, 0 findings); with an empty `M365AEGIS_HOME` the exe printed its version, `doctor --offline` exited 0, `whoami` exited 3; `release.ps1 -DryRun` green. The new fixtures were run through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — against the tenant, in the main session's signed-in round on the maintainer's machine over Remote
  Desktop, with `ExchangeMessageTrace.Read.All` consented and on the token but the service principal not yet
  provisioned: `trace --sender <the admin's address> --days 2` exit 8 `forbidden`, the service's sentence
  ("Service principal-less Authentication failed … Provisioning may take several hours") and the hint naming the
  permission and the provisioning step, 1 request id; `trace --sender x@contoso.com --days 11` exit 2 `usage`,
  "the message trace answers a window of at most 10 days, not 11", the `--until` hint, no request sent. The live
  trace of a known message lands after this pull request, once the maintainer provisions the service principal
  (docs/development.md, "The sign-in and the home folder")

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
