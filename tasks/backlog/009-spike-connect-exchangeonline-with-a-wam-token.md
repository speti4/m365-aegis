---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Spike: `Connect-ExchangeOnline -AccessToken` with a token the broker acquired for outlook.office365.com

What: a timeboxed try (one day) of the sidecar's intended sign-in: a silent MSAL token for
`https://outlook.office365.com/.default` from our app registration (`Exchange.Manage` consented), passed to
`Connect-ExchangeOnline -AccessToken -Organization <tenant>.onmicrosoft.com`; whether `-UserPrincipalName` is
also required, whether one token can carry `Exchange.Manage` and `Exchange.ManageV2`, and how the ~60-minute
expiry is renewed (a `reconnect` message with a fresh token). The result is a dated note here and D-004 amended.
Why: D-004 promises one sign-in for Graph and Exchange; if the token hand-off does not work, the sidecar falls
back to the module's own sign-in and the plan of M2 changes.
Done when: the note states which variant connected, with the parameters and the timings, against the real
tenant, and no token or tenant identifier is written into the repo.
