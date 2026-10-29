---
size: S
origin: done/007-message-trace-through-graph.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 035 — `trace --status` and `--subject` filters

## Context

`--status <delivered|failed|pending|…>` and `--subject <text>` on `trace`, sent as `$filter` terms where
the endpoint supports them and applied client-side where it does not, with the choice stated in the hint and
in `docs/development.md`.
Why: a busy sender's 10-day trace is hundreds of rows; the support question is the failed ones or one subject.
Done when: both filters answer from fixtures and live, and a filter the endpoint refuses is applied client-side
rather than answered with a 400.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the two options
- `docs/development.md`, the message trace: which filter is server-side and which client-side

## Not in this change

- the details per recipient (task 034)
- a saved or scheduled trace (M5)

## Steps

1. [x] The two options, the `$filter` terms the reference documents, the client-side fallback
2. [x] Fixtures for both, the Core and Cli tests, the snapshot
3. [x] Live check (main session): `--status failed` and `--subject` over a real sender
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference: the list documents `$filter`, `$top` and `$skiptoken`
  (https://learn.microsoft.com/graph/api/messagetracingroot-list-messagetraces, with the example
  `contains(subject, 'Weekly digest') and recipientAddress eq '…'`); the onboarding guide names the filterable
  fields (`id`, `messageId`, `receivedDateTime`, `recipientAddress`, `senderAddress`, `status`, `subject`, `toIP`),
  `contains`, `startswith` and `endswith` on `subject`, and the example `… and status eq 'delivered'`
  (https://learn.microsoft.com/exchange/monitoring/trace-an-email-message/graph-api-message-trace); the statuses of
  the resource: delivered, pending, failed, quarantined and the others the trace reports
  (https://learn.microsoft.com/graph/api/resources/exchangemessagetrace)
- 2026-10-29 — built: `trace --status <status>` and `--subject <text>`, both server-side as documented
  (`status eq '…'`, `contains(subject,'…')` after the window and the addresses); `--status` takes the service's
  names in any case (and `filtered-as-spam`) and refuses another as a usage error with the list. On a 400 the
  window is traced again without the two terms and they are applied client-side (the status exact, the subject a
  case-insensitive contains), so the answer is never the 400; `MessageTrace.filteredBy` (`service`, `client`, null
  without a filter) and a line under the table say which way it went. The 400 is told apart by the new
  `AegisException.HttpStatus`, which the Graph client sets on every failed answer (task 032 uses it too)

## Deviations from the plan

- none

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token): `trace --sender <an address> --days 10 --status failed` exit 0 in 0.9 s, 1 request, 0 rows, `filteredBy: service`; `--subject <a word>` exit 0 in 0.9 s, 1 request, 2 rows, `filteredBy: service`: the service took both terms, so the client-side path stays the fixture-tested fallback

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
