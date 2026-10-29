---
size: S
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 037 — `audit list --user <upn>`: the directory audit by the initiating user

## Context

A `--user <upn>` option on `audit list` that filters `auditLogs/directoryAudits` on
`initiatedBy/user/userPrincipalName` (the documented filter), combinable with `--days`, `--since` and
`--activity`.
Why: "what did this admin change" is the question after an incident, and the full day's list of a busy tenant
is a thousand rows (the live round of 2026-10-29 hit the limit).
Done when: the option answers from fixtures and live, and the footer names the user.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the option
- `docs/development.md`, the audit lookups: the filter

## Not in this change

- the footer of a truncated answer (task 038)
- a filter by the target object

## Steps

1. [x] The option, the `$filter` term against the reference, combinable with the window and `--activity`
2. [x] A fixture, the Core and Cli tests, the snapshot with the user in the footer
3. [x] Live check (main session): `audit list --user <upn> --days 1`
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the reference: the directoryAudit resource documents `$filter` (`eq`) on
  `initiatedBy/user/id`, `user/displayName`, `user/userPrincipalName`, `app/appId`, `app/displayName` and
  `startswith` on `user/userPrincipalName`; the list supports `$filter` (`eq`, `ge`, `le`, `startswith`), `$top`,
  `$orderby` and the skip token (https://learn.microsoft.com/graph/api/resources/directoryaudit,
  https://learn.microsoft.com/graph/api/directoryaudit-list)
- 2026-10-29 — built: `audit list --user <upn>` adds `initiatedBy/user/userPrincipalName eq '<upn>'` to the
  `$filter` after the window and `--activity`, with `$orderby=activityDateTime desc` as before; the value is
  trimmed and sent as given (unlike the sign-in log, the reference does not say the audit log keeps it in lower
  case); an empty `--user` is a usage error before anything is sent. The report carries `user`, and the line under
  the table says "initiated by <upn>"; an activity an application started never matches, which the option's help
  says by naming the user

## Deviations from the plan

- none

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token): `audit list --user <an admin's UPN> --days 1` exit 0 in 1.1 s, 1 request, 46 entries, every one initiated by that UPN, `user` in the envelope; with `--activity "Update user"` added exit 0 in 0.7 s, 1 request, 0 entries (no such activity by that user that day), so Graph takes the user filter with `startswith` and `$orderby`

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
