---
status: ready
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Auth: MSAL.NET with the Windows account manager, interactive and device-code fallback, `login`, `logout`, `whoami`

What: `Auth` in the core signs in as a public client of our own app registration (D-003) through MSAL.NET and
the broker, in the order task 013 measured (D-003, note of 2026-10-29): silent with `OperatingSystemAccount`, silent
with the profile's account chosen by user principal name among the broker's accounts, interactive through the
broker with the console's root owner as the parent window, device code when the broker answers
`window_handle_required`. The cache in a DPAPI-protected file under the home folder, shared by the command line and
the window; per-resource tokens for Graph and `outlook.office365.com`; `login`, `logout` and `whoami` through the
output contract; the registration documented without real values; the tenant profile in `config.json`.
Why: everything else signs in through this; the broker is why the stack is .NET (D-002).
Done when: `whoami` answers silently on the maintainer's machine, the device-code path answers in a redirected
shell, the tests cover the cache and profile logic offline with a fake token source, and no token reaches a log.
