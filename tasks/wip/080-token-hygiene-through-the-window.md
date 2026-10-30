---
size: S
origin: done/072-token-hygiene-test-across-logs-plans-and-output.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 080 — Token hygiene through the window: the bridge's sign-in and the `window:` log lines

## Context

The marker-token test of task 072 run through the window's path: the bridge's sign-in request, a lookup over the
Exchange token source, an error reply, and the `window:` log lines, with every file under the temp home, the bridge
replies and the window's stderr scanned for the marker.
Why: task 072 covers the command line and the shared Core sinks; the window writes its own `window:` lines and sends
replies over the bridge, which are sinks of their own.
Done when: the test is green over the bridge dispatcher with the fake token source, and injecting the marker into a
reply or a `window:` line on purpose makes it red.
Part of the window follow-ups bundle (077, 080) of the seventh round of 2026-10-30; the task session owns the steps
below and may reshape them.

## Docs that move

- `docs/development.md`, the safety tests section (task 072's entry): the window's path is covered too
- `SECURITY.md`, if it lists the token-hygiene test as a safeguard: the window named

## Not in this change

- The write-guard test over the bridge (task 079, waits for the first bridge write request)
- A change to what the `window:` lines carry beyond removing a leak the test finds

## Steps

1. [x] A test over the bridge dispatcher with the fake token source of task 072: a `login`, a lookup over the
       Exchange token source, an error reply, and the `window:` lines, every sink scanned for the marker (the temp
       home's files, the replies, stderr)
2. [x] A deliberate injection of the marker into a reply and into a `window:` line turns the test red (proven once,
       documented in the Log, not committed)
3. [x] Any leak the test finds is fixed in the window or the dispatcher (none found)
4. [x] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/077-window-follow-ups` with task 077
- 2026-10-30 — the token source: task 072's `MarkedTokenBroker` moved from a private class of the Cli test to
  `tests/shared/MarkedTokenBroker.cs`, linked into both test projects (the Cli test uses it unchanged). Two additions:
  an optional tenant id for the tokens (the Admin API's recorded answers are filed under
  `AdminApiConnection.TestTenantId`, so the window's mailbox lookups read them), and a device-code sign-in that hands
  its made-up prompt to the caller before it answers, so the bridge's `device-code` notification is a sink of the run.
- 2026-10-30 — `tests/M365Aegis.Core.Tests/BridgeTokenHygieneTests.cs`, one test over a temporary home with a saved
  profile, under `LogSink.UseVerbose(LogSink.ForHome(home))`, through one `BridgeDispatcher` over `AuthService` with the
  window's hints and the marked broker, a Graph client over `HttpGraphTransport` with the profile's Graph token and a
  handler that fails with `Authorization: Bearer <token>` in its exception's message, and a `WindowExchange` as
  `ForWindow` makes it over `ExchangeTokenSource.ForProfile(auth, null)` (sessions on the fake module, the Admin API on
  the recorded answers). The requests: `whoami`, `login`, `login` with `deviceCode`, `group-members` (the `internal`
  error reply, the stack trace in the log), `mailbox-list`, `mailbox-access` for a user (three sidecars connect with the
  marked token) and for a mailbox, `mailbox-list` on a second window whose sidecar the fake module refuses with the
  token in its message (the `auth` error reply, `<token>` in its message after the sidecar's `Hide-Secret`), `logout`.
  The sinks: every file under the home (`config.json`, the day's log with the four `window:` lines and the internal
  error), every reply and notification as `BridgeProtocol.Write` sends it to the page, and `Console.Error` captured for
  the run. The test also holds that the run did what it says (the reply types in order, the device code sent, the
  Graph bearer and the session's token are the marked ones, the `window:` lines written, stderr empty).
- 2026-10-30 — the injections, not committed, each a temporary edit of `BridgeDispatcher.ExchangeAsync` with the
  Exchange Online token from `exchange.Tokens.GetAsync`: into each mailbox reply's payload (`"injected"`), the test went
  red with `Collection: ["message 6 to the page", "message 7 to the page", "message 8 to the page"]` (the `mailboxes`,
  `user-grants` and `mailbox-holders` replies); appended to the `window:` line, red with `Collection:
  ["logs\m365-aegis-2026-10-30.log"]` (the opaque token passes the log's scrub patterns, so only a line that never takes
  it passes). The file was restored from a copy after each run and holds no trace of either.
- 2026-10-30 — no leak found: the dispatcher's replies carry the core's failures (the sidecar hides the token, an
  internal error's message is scrubbed), the `window:` lines carry counts and timings only, and the window's
  `Console.Error` lines (the smoke run's and a WebView2 start failure) are not on the dispatcher's path.

## Deviations from the plan

- `SECURITY.md` is unchanged: it names the sinks a token must never reach but lists no test as a safeguard, which is
  the condition under *Docs that move*. A follow-up proposes naming the window's bridge messages among its sinks.

## Verification

- 2026-10-30 — `BridgeTokenHygieneTests` green, and red under each injection above. The local matrix in the worktree,
  all exit 0: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`, `dotnet build
  --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings), `dotnet test --no-build -c Release` (1267 tests, 0
  failed; the Cli's `TokenHygieneTests` over the shared broker included), the coverage gate (`M365Aegis.Core.dll` and
  `m365-aegis.dll` at 100% lines and branches), `python -I scripts/changelog.py --check`, `python -I
  scripts/check-fixtures.py` (323 files, 0 findings), and the front end's check list (task 077 changed a sample).

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (`SECURITY.md` lists no test, see the deviation)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
