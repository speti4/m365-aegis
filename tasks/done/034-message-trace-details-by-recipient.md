---
size: S
origin: done/007-message-trace-through-graph.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 034 — Message trace details per recipient

## Context

`trace details <message id> [--recipient <address>]` over the trace endpoint's `getDetailsByRecipient`
function, listing the delivery steps of one message (the events with their timestamps, the status and the
detail text), through the Graph client and the output contract.
Why: the trace list says delivered or not; the support question is where a message stopped.
Done when: the details of a made-up message answer from fixtures and a live message's steps answer after one
sign-in, within the trace endpoint's throttling budget.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the subcommand
- `docs/development.md`, the message trace: the function and the row
- `tests/fixtures/README.md`: the new fixture

## Not in this change

- the trace through the Exchange sidecar (M2)
- the `--status` and `--subject` filters (task 035)

## Steps

1. [x] The subcommand, the request against the reference, the result record and the table view
2. [x] A fixture, the Core and Cli tests, the snapshot
3. [x] Live check (main session): the steps of one recent message
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference: `GET /admin/exchange/tracing/messageTraces/{exchangeMessageTraceId}/
  getDetailsByRecipient(recipientAddress='…')`, the recipient required, `ExchangeMessageTrace.Read.All`, the same
  prerequisites (the service principal) and the same 100 requests per 5 minutes as the list; no query parameters
  or paging documented; the answer a collection of `exchangeMessageTraceDetail` (`id`, `messageId`, `dateTime`,
  `event`, `action`, `description`, `data` as an XML string) (https://learn.microsoft.com/graph/api/exchangemessagetrace-getdetailsbyrecipient,
  https://learn.microsoft.com/graph/api/resources/exchangemessagetracedetail); the list filters on `id` and
  `messageId` and answers the last 48 hours without a window
  (https://learn.microsoft.com/graph/api/messagetracingroot-list-messagetraces,
  https://learn.microsoft.com/exchange/monitoring/trace-an-email-message/graph-api-message-trace)
- 2026-10-29 — built: `trace details <trace id | Message-ID> [--recipient <address>]`, a subcommand inside the
  `trace` command (Program.cs unchanged). The argument is a trace id (a GUID, the `id` of a `trace` row) or the
  Message-ID header (angle brackets added when missing). A trace id with `--recipient` is the one function call;
  otherwise the trace rows by `id eq` or `messageId eq` name the recipients and their status, at most 10 per
  command (a usage error past that asks for `--recipient`), whose steps go out side by side; no row is exit 4 with
  the 48-hour window and the `--recipient` route in the hint. The 401 of a missing service principal and a 403 are
  the trace's setup failure (exit 8, the same hint), shared through `MessageTraceLookup.IsSetupFailure`. The table is
  a row per step and recipient (time, recipient, event, action, the description cut to 80 characters); the
  envelope keeps the event's XML `data`. The fixture transport now writes an `@` inside a path segment as `%40`:
  the recipient's address in the function's path made the transport take it for the query's `@`

## Deviations from the plan

- The argument also takes the Message-ID header, and without `--recipient` every recipient (up to 10) is listed.

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token), with the trace's service principal provisioned: `trace --sender <an address> --days 2` exit 0 in 1.5 s, 1 request, 3 rows; `trace details "<a Message-ID>"` exit 0, three steps (Receive, Submit, Deliver) with their UTC times and detail text, the footer naming the Message-ID, the subject, the recipient as delivered and its trace id; `trace details <trace id> --recipient <address>` exit 0 in 1.0 s, 1 request

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
