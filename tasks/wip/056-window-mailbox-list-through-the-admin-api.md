---
size: S
origin: done/049-one-exchange-token-source-for-the-sidecar-and-the-admin-api.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 056 — The window's `mailbox-list` through the Admin API over the shared token source

## Context

The window holds only its sidecar session today (task 045); it gets the command line's layers (`ExchangeBackends`
over the one `ExchangeTokenSource` of task 049), so its `mailbox-list` request answers from the Admin API without
the sidecar and the other requests keep the warm session.
Why: the command line's list runs in 7.5 s without a pwsh child since task 048; the window still pays the connect
and then the cmdlet for the same list.
Done when: the window's shared list answers with no sidecar start in the `window:` log line, the access request
still uses the warm session, and the `--smoke` run starts no sidecar.
Part of the Exchange and window bundle (055, 059, 056, 057) of the round of 2026-10-30; the task session owns the
steps below and may reshape them. The live line (the `window:` log line of the shared list with no sidecar start,
then an access request on the warm session) comes from the main session's signed-in round, with the maintainer at
the picker. The parallel writes bundle does not touch the window or the bridge.

## Docs that move

- `docs/development.md`, "The front end" (or the window's section): the window's layers, which request uses which
- `README.md`, the bundle's own heading: the window's list without a sidecar

## Not in this change

- a task card or a plan on the bridge (waits for the colleague's review of 018)
- the window's forwarders or size pages (none exist)

## Steps

1. [x] The window's bridge host holds `ExchangeBackends` over one `ExchangeTokenSource` next to (or instead of) its
   `ExchangeSession`; `mailbox-list` answers from the Admin API, `mailbox-access` keeps the warm session
2. [x] The `window:` log line names the layer and the sidecar start count; `--smoke` starts no sidecar
3. [x] Core bridge tests (the dispatcher over fixtures and the fake module) and the front-end samples unchanged
   or updated together
4. [x] Docs
5. [x] Live line (main session): the `window:` lines of a shared list and an access request

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window bundle (055, 059, 056, 057)
- 2026-10-30 — `Bridge/WindowExchange.cs` gets a second constructor over a token-source factory, a session factory
  and an Admin API factory: the source is made on the first request and shared by the session (its connect, its
  renewals, the keep-warm reconnects) and by an Admin API client made per request over it (the HTTP client is shared,
  so a client per request costs nothing and keeps each request's ids in its own log); `Backends(graph)` gives a
  request an `ExchangeBackends` with the holder itself as the sidecar. A sign-out drops the source with the session.
  `BridgeServices.ForWindow` wires it with the command line's parts (`ExchangeConnection.Tokens`, `Options` over the
  source, `AdminApiConnection.Create` over the source: the fixtures under `M365AEGIS_GRAPH_FIXTURES`, none with only
  `M365AEGIS_EXO_TEST_ORGANIZATION`). The dispatcher's `mailbox-list` runs `MailboxLookups.ListAsync(backends, …)`;
  `mailbox-access` runs on `backends.Sidecar`, the warm session, as before. The `window:` line now reads
  `mailbox-list answered in … ms through admin-api; exchange no sidecar, token acquisitions 1`. The old constructor
  stays (a holder without an Admin API).
- 2026-10-30 — tests in `BridgeProtocolTests` (3): the list from the Admin API equals the `mailboxes` sample with no
  session made, then the access equals `mailbox-holders` on one sidecar start, both over one token acquisition; a
  sign-out drops the token source and the next list makes a new one; a holder without an Admin API lists on its
  session. The bridge samples and the front end's payloads are unchanged.

## Deviations from the plan

- none

## Verification

- 2026-10-30, local (the worktree): `dotnet test -c Release` 775 passed (3 new bridge tests; the 45 earlier bridge and
  session tests unchanged); the `windows` job's steps with `M365AEGIS_HOME` at an empty temporary folder:
  `m365-aegis-gui.exe --smoke` exit 0 ("ready after 762 ms, window … (top-level)"), no `aegis-exo-sidecar` pwsh
  process afterwards; Vitest 57 passed, the front end's lint, typecheck and build green.
- 2026-10-30, live (the main session's signed-in round; the window from this branch with `--verbose`, the real home,
  the maintainer at the picker): the shared list, then one mailbox's access. `window: mailbox-list answered in 8040 ms
  through admin-api; exchange no sidecar, token acquisitions 1`, with the pwsh count unchanged (2 before the window,
  2 after the list); then `window: mailbox-access answered in 5201 ms through pwsh; exchange session 1, sidecar starts
  1, renewals 0, last connect 538 ms, token acquisitions 1`, with one pwsh more (the one sidecar). The process logged
  one `exchange-online: exchange token acquisition 1: one token for every layer of the command, valid for 35 min`
  line: the list and the sidecar's connect shared one token. Before this task the window's list started the sidecar
  (task 049's round, `mailbox-list answered in 4289 ms; exchange session 1, sidecar starts 1`).
- 2026-10-30, the bundle's final local matrix (the worktree, after 055's read went back to the sidecar): `dotnet
  restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c Release` (0 warnings), `dotnet test`
  770 passed, `changelog.py --check` up to date, `check-fixtures.py` 275 files 0 findings, `npm ci`, lint, typecheck,
  Vitest 57 passed, build; `Invoke-Pester -Path tests/sidecar` 39 passed; PSScriptAnalyzer (Warning, Error) over the
  sidecar, `tests/sidecar` and `scripts`: no findings; the `windows` job's steps with an empty `M365AEGIS_HOME`:
  `--version` 0, `--json doctor --offline` 0, `--json whoami` 3, `m365-aegis-gui --smoke` 0 (ready after 545 ms,
  top-level window, no sidecar process), `scripts/release.ps1 -DryRun` 0.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
