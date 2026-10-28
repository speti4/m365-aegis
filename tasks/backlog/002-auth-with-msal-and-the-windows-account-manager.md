---
status: ready
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Auth: MSAL.NET with the Windows account manager, interactive and device-code fallback, `login`, `logout`, `whoami`

What: `Auth` in the core signs in as a public client of our own app registration (D-003) through MSAL.NET and
the broker: silent first, the account picker when silent fails, device code where there is no window; the cache
in a DPAPI-protected file under the home folder, shared by the command line and the window; per-resource tokens
for Graph and `outlook.office365.com`. The commands `login`, `logout` and `whoami` (account, tenant, scopes,
token expiry) through the output contract. The app registration (platform, redirect URI, scopes, consent) is
documented in `docs/development.md` without real values; the tenant profile goes to `config.json` under the home.
Why: everything else signs in through this; the broker is why the stack is .NET (D-002).
Done when: `whoami` answers silently on the maintainer's Entra-joined machine, the device-code path answers in a
redirected shell, the tests cover the token-cache and profile logic offline with a fake token source, and no
token ever reaches a log or an envelope.
