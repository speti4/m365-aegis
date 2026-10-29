---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M1 Foundation
---

# 009 — Spike: `Connect-ExchangeOnline -AccessToken` with a token the broker acquired for outlook.office365.com

## Context

A timeboxed try (one day) of the sidecar's intended sign-in: a silent MSAL token for
`https://outlook.office365.com/.default` from our app registration (`Exchange.Manage` consented), passed to
`Connect-ExchangeOnline -AccessToken -Organization <tenant>.onmicrosoft.com`; whether `-UserPrincipalName` is
also required, whether one token can carry `Exchange.Manage` and `Exchange.ManageV2`, and how the ~60-minute
expiry is renewed (a `reconnect` message with a fresh token). The result is a dated note here and D-004 amended.
Why: D-004 promises one sign-in for Graph and Exchange; if the token hand-off does not work, the sidecar falls
back to the module's own sign-in and the plan of M2 changes.
Done when: the note states which variant connected, with the parameters and the timings, against the real
tenant, and no token or tenant identifier is written into the repo.
Part of the Exchange bundle (009, 015, 016, 011, 010) of the round of 2026-10-29: the spike is not a separate
code path, it is the sidecar's `connect` of task 015 run live; the live run is the main session's, the task
session writes the dated note and a proposed amendment text for D-004 that the main session files.

## Docs that move

- `tasks/decisions/D-004-…`: a dated note on the token hand-off (proposed by the session, filed by the main session)
- `docs/development.md`, the Exchange Online sidecar: the connect's parameters

## Not in this change

- the Admin API (task 010)
- the connect timings per variant (task 011)

## Steps

1. [x] The connect of task 015 takes the broker's token (`-AccessToken`, `-Organization`), the renewal as a
       `reconnect` before the expiry; `-UserPrincipalName` only if the live run shows it is needed
2. [x] Live run (main session): which variant connected, the parameters, the timing; whether one token carries
       both `Exchange.Manage` and `Exchange.ManageV2`
3. [x] The dated note here and the proposed D-004 amendment text in the final report

## Log

- 2026-10-29 — promoted by the main session into the Exchange bundle (009, 015, 016, 011, 010)
- 2026-10-29 — the connect of task 015 passes the broker's token for `https://outlook.office365.com/.default`
  (`AuthService.GetTokenAsync`, silent) as `-AccessToken` with `-Organization <tenant>.onmicrosoft.com` (the
  profile's tenant, or the initial domain from `GET /organization`); `M365AEGIS_EXO_CONNECT=upn|both` passes
  `-UserPrincipalName` instead or as well, so the live run can try each variant. The renewal is a `reconnect` five
  minutes before `ExpiresOn`. `m365-aegis probe exchange-connect [--connect-with organization|upn|both]` times
  the start, the import, the connect, a first cmdlet, a reconnect with a fresh token and a cmdlet after it, and
  prints the token's permissions; no token or tenant identifier
- 2026-10-29 — the note: `Connect-ExchangeOnline -AccessToken <the broker's token for
  https://outlook.office365.com/.default> -Organization <tenant>.onmicrosoft.com -SkipLoadingFormatData
  -CommandName Get-AcceptedDomain -ShowBanner:$false` connected at the first try in the tenant (the variant with
  the organization; `-UserPrincipalName` is not needed, so `M365AEGIS_EXO_CONNECT` stays a diagnostic switch).
  One token carries both `Exchange.Manage` and `Exchange.ManageV2` (with `User.Read` and `User.ReadBasic.All`,
  the `.default` consent of the resource). Cold, on the maintainer's machine over Remote Desktop with
  ExchangeOnlineManagement 3.10.1 and pwsh 7.6.9: start to `hello` 464 ms, module import 1060 ms, connect 1290
  ms, first cmdlet 1445 ms; a `reconnect` with a fresh token 375 ms, a cmdlet after it 186 ms. The token had 83
  minutes left (the identity platform's lifetime varies; the session reads `ExpiresOn`). The module's own sign-in
  is not needed as a fallback

## Deviations from the plan

- No fallback to the module's own sign-in was built: the live run showed it is not needed.

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the
  Release build with the front end (0 warnings), `dotnet test` 388 green (the Core and Cli tests start the sidecar
  as a real pwsh child against the fake module), `Invoke-Pester -Path tests/sidecar` 20 green, PSScriptAnalyzer
  over the three folders CI checks 0 findings, `changelog.py --check`, `check-fixtures.py` (136 files, 0
  findings); with an empty `M365AEGIS_HOME` `doctor --offline` exited 0 (`sidecar-script` passes on the changed
  script), `whoami` 3, `mailbox list` 3; the window's `--smoke` 0; `release.ps1 -DryRun` green. The new test data
  went through the anonymizer scan, which flagged only the made-up contoso, fabrikam and example addresses
- 2026-10-29 — against the tenant, in the main session's signed-in round (evening, UTC+1) from this branch's
  Release build: `whoami` exit 0, the Exchange token from the cache carrying `Exchange.Manage Exchange.ManageV2
  User.Read User.ReadBasic.All`; `probe exchange-connect` exit 0 at the first try with `-Organization`, the
  timings of the note above, "Manage and ManageV2 on one token: yes"; every mailbox command of tasks 015 and 016
  then ran with the default variant (`M365AEGIS_EXO_CONNECT` unset)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
