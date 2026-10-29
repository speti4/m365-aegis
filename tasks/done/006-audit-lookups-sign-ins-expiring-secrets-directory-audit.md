---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 006 — Audit lookups: sign-ins per user and failures, expiring application secrets, the directory audit

## Context

`signin list --user <upn> [--failures] [--days N]` over `auditLogs/signIns`; `secrets expiring <days>` over the
applications' `passwordCredentials` and `keyCredentials` (and the service principals'); `audit list --days N
[--activity …]` over `auditLogs/directoryAudits`; the time window and the paging in the client (task 003), the
filters in the command.
Why: the weekly report and the "what happened" questions of the audit domain, which the agent path answers
slowly and the admin centers one page at a time.
Done when: each command answers from fixtures and live (the live check runs in the main session's signed-in
round), `secrets expiring 30` lists the credentials that expire within a month with the application's name, and
a tenant without Entra P1 gets a clear error for the sign-in log instead of a stack trace.
Built on the Graph client of task 003 from a stacked branch (main session's decision of 2026-10-29): the branch
rebases onto the Graph bundle's branch once its client is pushed, never changes the client's public surface
(a needed change is asked of the Graph bundle's session through the main session), and opens its pull request
only after the Graph bundle is merged and the branch is rebased onto `main`. The task session owns the steps
below and may reshape them.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the delegated permissions these lookups need
  (`AuditLog.Read.All`, `Directory.Read.All`, `Application.Read.All` or what the reference settles), one line each
- `README.md`: the lookups in the first-run section
- `tests/fixtures/README.md`: the new fixtures

## Not in this change

- the Graph client itself (task 003)
- a saved weekly report or a scheduled run (M5 snapshot)
- the message trace (task 007)

## Steps

1. [x] `Audit/AuditWindow` in the Core: `--days N` (1 by default, 1 to 30) or `--since <date>` (UTC unless an
       offset is named), never both, cut to whole seconds and written as the OData date-time literal; `--limit N`
       (1000 by default) caps a log lookup, the newest first, with a `truncated` flag
2. [x] `signin list [--user <upn>] [--failures]` over `auditLogs/signIns`: `createdDateTime ge` and
       `userPrincipalName eq` (lower case, as the log keeps it) in the `$filter`, the pages the client's, the
       failures picked client-side (the log documents only `eq` on `status/errorCode`); a 403 for a tenant
       without Microsoft Entra ID P1 or P2 becomes its own sentence (exit 8), any other 403 names
       `AuditLog.Read.All` and the reader roles
3. [x] `secrets expiring <days> [--include-expired]` over `applications` and `servicePrincipals` with
       `$select=id,appId,displayName,passwordCredentials,keyCredentials&$top=999`, the expiry computed
       client-side against one moment, the application's name, the owner (app registration or service principal),
       the kind (secret or certificate), the credential's name or key id, the days left; the soonest first
4. [x] `audit list [--activity <name>]` over `auditLogs/directoryAudits`: `activityDateTime ge` and
       `startswith(activityDisplayName, …)` in the `$filter`, `$orderby=activityDateTime desc` (the reference
       states no default order); who did it (the user's principal name or the application's name) and the targets
5. [x] The result records (`SignInReport`, `CredentialExpiryReport`, `DirectoryAuditReport`), the table views, the
       hand-made fixtures, the envelope and table snapshots, the in-process and child-process tests
6. [x] Docs: the permissions in `docs/development.md`, the lookups in the README's first run, the fixtures README
7. [x] Live round in the main session's signed-in round: `signin list --user <upn> --days 1`, `--failures`,
       `secrets expiring 30`, `audit list --days 1`
8. [x] The checks of CONTRIBUTING.md, the fixture check and the anonymizer scan, the changelog, `tasks_check`,
       the close; the pull request once the Graph bundle is merged and the branch rebased onto `main`

## Log

- 2026-10-29 — promoted by the main session; stacked on the Graph bundle's branch (003, 005, 007)
- 2026-10-29 — rebased onto `origin/task/003-graph-client-and-group-lookups` (`2408161`, the Graph client); the
  client's 136 tests green before anything was added; the client's public surface is used as it is
- 2026-10-29 — the endpoints checked against the Microsoft Graph v1.0 reference before coding:
  [List signIns](https://learn.microsoft.com/en-us/graph/api/signin-list?view=graph-rest-1.0) and the
  [signIn resource](https://learn.microsoft.com/en-us/graph/api/resources/signin?view=graph-rest-1.0) (least
  privileged delegated permission `AuditLog.Read.All` alone, roles Global Reader, Reports Reader, Security
  Administrator/Operator/Reader; a Microsoft Entra ID P1 or P2 license; 1000 to a page by default and at most, newest
  first; `createdDateTime` takes `ge`/`le`, `userPrincipalName` `eq`/`startsWith` and is kept in lower case,
  `status/errorCode` only `eq`), [List directoryAudits](https://learn.microsoft.com/en-us/graph/api/directoryaudit-list?view=graph-rest-1.0)
  and the [directoryAudit resource](https://learn.microsoft.com/en-us/graph/api/resources/directoryaudit?view=graph-rest-1.0)
  (`AuditLog.Read.All`; `activityDateTime` `eq`/`ge`/`le` and `$orderby`, `activityDisplayName` `eq`/`startswith`),
  [List applications](https://learn.microsoft.com/en-us/graph/api/application-list?view=graph-rest-1.0)
  (`Application.Read.All`; 100 to a page by default, 999 at most; a `$select` of `keyCredentials` is held to 150
  requests a minute per tenant, one request per page here), the
  [activity-log troubleshooting](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-analyze-activity-logs-with-microsoft-graph)
  page (the "tenant doesn't have premium license" error of a tenant without P1)
- 2026-10-29 — built: `Audit/` in the Core (the window, the three lookups, their result records), `signin list`,
  `secrets expiring`, `audit list` with their table views; 38 new tests, 174 green (the window rules, the filters,
  the pages, the client-side picks, the cap stopping before the next page, both 403s, the envelopes and the tables
  as snapshots at a fixed moment, the child-process runs over the fixtures with an explicit `--since`, so the
  request matches a fixture whatever the date); the fixtures written by hand from the documented shapes; the fixture
  check green; the anonymizer scan finds only the `contoso.com` addresses and a CARD false positive on the made-up
  GUIDs (the zeros pass the Luhn check)
- 2026-10-29 — decided while building: the missing P1 license stays `forbidden` (exit 8) with a sentence and a
  hint of its own instead of a new exit code, because the service refuses the call as it does for a missing
  permission; `--user` is optional (the failed sign-ins of every user is the other everyday question);
  `--since` next to `--days` (an explicit start, and child-process tests that do not depend on the date); the
  expired credentials only with `--include-expired`, so the weekly question shows what is about to break
- 2026-10-29 — rebased onto the Graph bundle's `ac84fd5` (005 and 007; the client's surface grew only): with
  `$orderby`, the `--activity` fixture's path came to 271 characters in this worktree, past the 260 git checks out,
  so the fixtures whose name passes 120 characters (the `--activity` filter, alice's second page) are stored in the
  transport's short form (`FixtureGraphTransport.ShortRelativePath`); the merge kept both lookup paragraphs of the
  README and put the audit permissions into the bundle's list in `docs/development.md`; 211 tests green

## Deviations from the plan

- `Directory.Read.All` is not needed: the reference names `AuditLog.Read.All` alone for both logs; the Context
  named it as a guess
- added beyond the Context: `--since`, `--limit`, `--include-expired`; `--user` of `signin list` is optional

## Verification

- 2026-10-29 — offline: 211 tests green on the Graph bundle's `ac84fd5` (38 of them this task's), `dotnet format`
  clean, the fixture check (115 files, 0 findings), the changelog check, the Windows job's steps (the full build
  with the front end, `--version`, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the release dry
  run); the scripted 403 of a tenant without P1 exits 8 with its own sentence, no stack trace
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the build of `46832c3`,
  the maintainer's tenant, which has Microsoft Entra ID P1; `AuditLog.Read.All` and `Application.Read.All`
  consented, a fresh device-code login, a Global Administrator account): `signin list --user <the admin> --days 1`
  exit 0 in 3.0 s, one request, 40 sign-ins newest first, not truncated; `--failures` exit 0, 3 failed sign-ins;
  `secrets expiring 30` exit 0 in 5.7 s cold, 11 requests (the service principals paged), about 80 app
  registrations and 900 service principals checked, one application certificate 18 days from expiry with the
  application's name; `--include-expired` exit 0, 41 credentials; `audit list --days 1` exit 0, 1000 entries newest
  first, truncated at the `--limit` default; `--activity "Update user"` exit 0, 23 entries, so Graph takes
  `$orderby=activityDateTime desc` with the `startswith` filter; `--days 1 --since 2026-10-28` exit 2 with the
  usage sentence. The P1 error path was not met live (the tenant has P1); the fixture test covers it

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: YYYY-MM-DD`
