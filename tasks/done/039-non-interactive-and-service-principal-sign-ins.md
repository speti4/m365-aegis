---
size: M
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 039 — Non-interactive and service principal sign-ins

## Context

`signin list` gains `--kind interactive|non-interactive|service-principal|managed-identity` over the beta
endpoint's `signInEventTypes` filter (v1.0 lists interactive user sign-ins only), through the client's beta
switch, with the beta dependency named in the hint and in `docs/development.md`.
Why: a leaked application secret or a token replay shows up in the non-interactive and service principal logs,
which the v1.0 answer never lists.
Done when: each kind answers from fixtures and live, and the command says which endpoint version answered.
Part of the Graph follow-ups bundle (032–039) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `README.md`, the first-run lookups: the option
- `docs/development.md`, the audit lookups: the beta dependency and the kinds
- `tests/fixtures/README.md`: the beta fixtures

## Not in this change

- a risk or conditional-access view of the sign-ins
- the v1.0 path's behavior without `--kind`

## Steps

1. [x] The option and the beta request (`signInEventTypes`) against the beta reference; the answer names the
       endpoint version
2. [x] A fixture per kind, the Core and Cli tests, the snapshots
3. [x] Live check (main session): each kind over one day
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Graph follow-ups bundle (032–039)
- 2026-10-29 — checked against the beta reference: the `signIn` resource's `signInEventTypes` (`interactiveUser`,
  `nonInteractiveUser`, `servicePrincipal`, `managedIdentity`; `$filter` `eq`, `ne`), "only interactive sign-ins are
  returned unless you set an explicit filter", the documented form `signInEventTypes/any(t: t eq
  'nonInteractiveUser')` beside a `createdDateTime` window, `servicePrincipalName` and `resourceDisplayName` on the
  resource, `AuditLog.Read.All` with Global Reader, Reports Reader or a Security role, P1 or P2, 1000 to a page at
  most, newest first (https://learn.microsoft.com/graph/api/resources/signin?view=graph-rest-beta,
  https://learn.microsoft.com/graph/api/signin-list?view=graph-rest-beta)
- 2026-10-29 — built: `signin list --kind <kind>` sends the request with `GraphVersion.Beta` and the event type in
  the `$filter` after the window and the user; without `--kind` the v1.0 request is the one of task 006. The report
  gains `kind` and `endpoint` (`v1.0` or `beta`, also on the v1.0 answer) and the entries `servicePrincipalName` and
  `resourceDisplayName`; the table of a service principal or managed identity list shows the service principal and
  the resource in place of the user and the app, and every beta table ends with the line that names the beta
  dependency. `--user` with an application kind, and an unknown kind, are usage errors before anything is sent.
  One made-up beta fixture per kind

## Deviations from the plan

- The v1.0 answer gained two fields (`endpoint`, and `resourceDisplayName` on each sign-in); its request and table
  are unchanged.

## Verification

- 2026-10-29 — offline, in the worktree: the whole local matrix green on the bundle's tip (`dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end with 0 warnings, `dotnet test` 371 tests, `changelog.py --check`, `check-fixtures.py` 0 findings, `doctor --offline` exit 0, `whoami` exit 3 with an empty home, the window's `--smoke`, `release.ps1 -DryRun`); the new fixtures through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — live, in the main session's signed-in round on the maintainer's machine (the worktree's Release build, a cached token), `signin list` over one day: without `--kind` exit 0 in 6.3 s, 2 requests, 1000 sign-ins (truncated), `endpoint: v1.0`; `--kind interactive` exit 0 in 6.9 s, 2 requests, 1000 (truncated), `endpoint: beta`; `--kind non-interactive` exit 0 in 7.4 s, 2 requests, 1000 (truncated), beta; `--kind service-principal` exit 0 in 5.6 s, 2 requests, 1000 (truncated), beta; `--kind managed-identity` exit 0 in 1.6 s, 1 request, 0 sign-ins, beta. No 400: beta takes `signInEventTypes/any(t: t eq '…')` with the window. The counts stop at the limit, so the interactive kind and v1.0 compare only as both over 1000; the line that names the beta dependency is in the table snapshot of the service principal list

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
