---
size: M
origin: done/015-sidecar-protocol-allow-list-and-connect.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M4 Window
---

# 045 — The window holds one `ExchangeSession` through the bridge

## Context

The window starts one sidecar on the first mailbox request and keeps it alive (`ExchangeSession` of task
015 is built for it: connect with the broker's token, `reconnect` before the expiry, a dead sidecar replaced), the
bridge gains the mailbox requests the window's pages need, and the session is closed with the window.
Why: every command-line mailbox lookup pays the 2.4 s cold connect (task 011); a warm sidecar in the window
answers in the cmdlet's own time, which is what the roadmap's "warm sidecar" of M4 means.
Done when: two mailbox lookups in a row from the window start one pwsh process, the second answers without a
connect, and the `--smoke` run still starts no sidecar.
Part of the window M4 bundle (040, 045, 018) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check runs in the main session's signed-in round. The Exchange M2 bundle
(041–044) changes the mailbox lookups and may add a second backend behind the same interface in parallel: the
window consumes the lookups through their existing interface and does not change them.

## Docs that move

- `docs/development.md`, "The front end" and "The Exchange Online sidecar": the window's session, its lifetime,
  the bridge's mailbox requests
- `README.md`: the window's mailbox page under the bundle's own heading
- `tests/` bridge contract samples: the mailbox requests and answers

## Not in this change

- the mailbox lookups themselves and their backends (the Exchange M2 bundle, 041–044)
- writes from the window (M3, then the task cards)

## Steps

1. [x] One `ExchangeSession` owned by the window's host, started on the first mailbox request, renewed before
   the token's expiry, replaced when the sidecar dies, disposed with the window
2. [x] The bridge's mailbox requests the window's first mailbox page needs, and that page in the front end
3. [x] Tests: the session's lifetime against the fake module, the bridge contract samples; `--smoke` starts no sidecar
4. [x] Live check (main session): two lookups in a row start one pwsh process, the second answers without a connect
5. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the window M4 bundle (040, 045, 018)
- 2026-10-29 — the session: `Core/Bridge/WindowExchange.cs` holds one `ExchangeSession` (made lazily on the first
  cmdlet, an `IExoCmdletRunner` the lookups take as they are), built by `BridgeServices.ForWindow` from
  `ExchangeConnection.Options` over the window's own `AuthService` (pwsh from `PATH`, the script after its hash check,
  the token silently, the organization; `M365AEGIS_EXO_TEST_ORGANIZATION` as on the command line). The session's own
  renewal (five minutes before the expiry, on the next cmdlet) and dead-sidecar replacement are task 015's; the
  holder adds a keep-warm timer (`TimeProvider`, every minute) that reconnects seven minutes before the expiry, so a
  request after an idle hour finds a fresh connection; a tick that cannot renew is left to the next request.
  `logout` ends it (`ResetAsync`), `Program` disposes it after the message loop ends. `MailboxLookups`,
  `ExchangeSession`, `ExchangeConnection`, the allow-list and the script are consumed through their public or
  internal members unchanged (no edit to any of them).
- 2026-10-29 — the bridge and the page: `mailbox-list` (`{"shared"}`) → `mailboxes`, `mailbox-access` (`{"mailbox"}`
  or `{"user"}`) → `mailbox-holders` or `user-grants`; a partial reverse lookup is the `user-grants` reply with its
  `failed` list; samples `mailbox-list`, `mailboxes`, `mailbox-access`, `mailbox-holders`, `user-grants` (taken from
  `--json mailbox …` against the fake module). The mailbox access card: a mailbox or a user, the shared mailboxes to
  pick from, the table, the partial warning, the answer's time under it. Under `--verbose` each mailbox request logs
  a `window:` line with the time and the session's starts, renewals and last connect.
- 2026-10-29 — tests: 7 in `Exchange/WindowExchangeTests.cs` (nothing before the first cmdlet, one sidecar for every
  cmdlet, the keep-warm tick with a manual clock, a tick that cannot renew, the sign-out, the close, a refused
  cmdlet that makes no session) and 11 in `BridgeProtocolTests` (the three samples, two requests on one sidecar with
  one connect, not found, five malformed requests, no session), all over a real pwsh child and the fake module.

## Deviations from the plan

- "The Exchange Online sidecar" of `docs/development.md` did not change: the Exchange M2 bundle (041–044) edits that
  section in parallel, and its sentence on the window ("the window one for its lifetime … replaced on the next
  cmdlet") stays true; the window's session is described in "The front end" instead.
- No new member on `ExchangeSession`: the holder reads `Token`, `Connection`, `Starts`, `Renewals` and `RenewAsync`,
  and the tests the internal `Client`.

## Verification

- 2026-10-29 — offline, by the task session: the Release window with an empty `M365AEGIS_HOME`, `--verbose`,
  `M365AEGIS_EXO_TEST_ORGANIZATION=contoso.onmicrosoft.com` and `M365AEGIS_EXO_MODULE_ROOT` on the fake module, driven
  over the WebView2 debugging port: no pwsh child before a request; `mailbox-access` of `finance@contoso.com`, then of
  the user `bob@contoso.com`: one pwsh child of the window, the log's `window:` lines "answered in 541 ms; exchange
  session 1, sidecar starts 1, renewals 0, last connect 6 ms" and "answered in 19 ms; … sidecar starts 1, renewals
  0, last connect 6 ms" (the same connect); the window closed with exit 0 and no sidecar left. `--smoke` exit 0,
  dispatching nothing.
- 2026-10-29 23:37–23:52 (UTC+1) — live, in the main session's signed-in round, this branch's Release window with
  `--verbose` against the real home: no sidecar before the first mailbox request; "List the shared mailboxes", one of
  them picked, then a lookup by user. The three `window:` lines: `mailbox-list answered in 6318 ms`, `mailbox-access
  answered in 2336 ms`, `mailbox-access answered in 2267 ms`, each with "session 1, sidecar starts 1, renewals 0,
  last connect 1484 ms": one pwsh start, the one connect reused by the later requests, no reconnect (the 2.3 s are
  the access lookups' own cmdlets against the service). The sidecar was not counted while alive (the sign-out came
  first); the evidence for one process is `sidecar starts 1` across the three lines, a count of 0 right after "Sign
  out", and 0 again with the process gone after the window closed.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
