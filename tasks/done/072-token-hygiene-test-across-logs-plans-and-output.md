---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 072 — A token-hygiene test: a marked fake token never reaches a log, a plan record, an envelope or stderr

## Context

The test auth service hands out tokens that carry a unique marker; the test drives `login`, `whoami`, a
`--verbose` run with the MSAL log on, a plan apply with `--yes`, a failed apply and an error envelope, then scans
every file under the temp home folder (`logs\`, `plans\`, `config.json`) and the captured stdout and stderr for
the marker; the sidecar's `Hide-Secret` is covered on the Pester side with the same marker in a `Connect` reply.
Why: SECURITY.md's first scope item is a token written to a log, a plan record, an envelope or stderr; the
scrubbing exists in several places (the MSAL log filter, the plan record, the sidecar) and no test holds them
together.
Done when: the test is green, and injecting the marker into any of those sinks on purpose makes it red.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. The fake token source is the test's own (the fixtures' auth samples and the
fake module, never MSAL against a tenant).

## Docs that move

- `docs/development.md`: one sentence under the tests naming the hygiene test and the marker convention
- `tests/fixtures/README.md` only if a new fixture carries the marker

## Not in this change

- A change to where the product scrubs (a gap the test finds is fixed in the bundle and named under Deviations)
- The window's own log (`window:` lines) beyond what the shared Core sinks cover

## Steps

1. [x] The marked token in the test auth path (the fixture transport's or the test token broker's token), the runs
       listed in the Context over a temp home, the scan of every file and of stdout and stderr
2. [x] The Pester case: `Hide-Secret` over a `Connect` reply that carries the marker
3. [x] The proof that the test is red when the marker is injected on purpose (a Log line with the injection and the
       failure, the injection not committed)
4. [x] The docs sentence

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)
- 2026-10-30 — the token source: the fixture transport asks for no token at all, so a child process over the fixtures
  never holds one and a scan of its output would pass for nothing. The test runs in process instead, through the
  product's parts with a test broker behind `ITokenBroker` (`MarkedTokenBroker`: every path signs Alice in; the Graph
  token `eyJ<marker>…` in a JWT's shape, the Exchange token `opaque-<marker>-exchange`, the marker `aegismarker` and a
  fresh GUID). No product seam was needed: `AuthService(home, brokerFactory)`, `HttpGraphTransport(token, handler)`,
  `CommandRunner.RunAsync(…, output, error, logInternalError)` and `ExchangeSessionOptions` take their parts already.
- 2026-10-30 — `tests/M365Aegis.Cli.Tests/TokenHygieneTests.cs`, one test over a temporary home, each answer rendered
  through `CommandRunner` as the envelope and as the table: `login` (writes `config.json`) and `whoami` (both tokens
  shown by expiry and scopes); the verbose log through `MsalTokenBroker.ToSink` into `LogSink.ForHome(home)` with the
  lines the library writes around a token (`Authorization: Bearer`, `?code=`, a JSON body with `access_token` and
  `refresh_token`, a bare JWT) and a line flagged as PII with the opaque token; `GraphCall.RunAsync` of
  `call PATCH … --yes` over `HttpGraphTransport` and a handler answering 204 for one group and 403
  `Authorization_RequestDenied` for another (4 records under `plans\`), and a GET the handler fails with
  `HttpRequestException` (`service-error`); an internal error whose message carries `Authorization: Bearer <token>`
  (the envelope, and the stack trace in `logs\`); and the sidecar over the fake module through `ExchangeTokenSource`,
  connected (6 mailboxes) and then refused by the fake with the token in its message (`auth`, the message reading
  `the service refused the token <token> (401)`). Then every file under the home and both streams are searched for
  the marker first (a leak names its sink), and the run is held to what it claims: the profile, 4 records, a log, the
  6 Graph requests each with the token as the bearer, the mailboxes listed, the four failures. 1.7 s.
- 2026-10-30 — the Pester case in `tests/sidecar/sidecar.Tests.ps1` ("keeps a marked token out of the protocol and
  stderr, connected and refused"): a connect with an opaque marked token, a read, a connect to the organization the
  fake refuses with the token in its message; the reply reads `<token>`, and neither the protocol lines nor stderr
  hold the marker. `Invoke-Pester -Path tests/sidecar`: 46 passed; PSScriptAnalyzer over `tests/sidecar`: 0 findings.
- 2026-10-30 — red proofs (none committed, each reverted with `git checkout` and the test green again): the access
  token as the sign-in result's display name in `AuthService` → `Assert.Empty() Failure: ["stdout"]`; `Hide-Secret`
  without the held secrets (`foreach ($secret in @())`) → `["stdout", "stderr"]`, and the Pester case red on
  `Expected regular expression 'the service refused the token <token>' to match 'fake: the service refused the token
  opaque-aegismarker…-exchange (401)'`; `LogSink.ScrubPatterns` without its bearer and JWT patterns →
  `["stdout", "stderr", "logs\m365-aegis-2026-10-30.log"]`.
- 2026-10-30 — docs: `docs/development.md` "The safety tests", the token hygiene paragraph with the marker convention.
  No fixture carries the marker (it is made at run time), so `tests/fixtures/README.md` does not change.

## Deviations from the plan

- In process, not as child processes: with the fixtures no token is ever asked for, and a fake broker in a child would
  need a switch in the shipped exe that signs in with made-up tokens. The in-process run uses the same parts the
  command line wires (the sign-in, the HTTP transport, the runner and its renderer, the log, the plan store, the
  sidecar), with the streams and the home given to them.
- The plan apply and the failed apply are `call PATCH --yes` over a handler rather than a group write over the
  recorded answers: the recorded answers' transport sends no token, and `call` goes through the same `PlanGuard` and
  `PlanStore` as every write.
- The MSAL log is driven through `MsalTokenBroker.ToSink`, the callback the product registers, with lines in the
  shapes the library writes; MSAL itself is not run (no tenant).
- No scrubbing gap was found, so no product code changed for this task.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (905 tests, 0 failed, 0 skipped on Windows; 808 before the bundle), `python -I
  scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (318 files, 0 findings); `Invoke-Pester -Path
  tests/sidecar` 46 passed; PSScriptAnalyzer over the sidecar, its tests and `scripts` 0 findings; the `windows` job's
  command line steps over an empty `M365AEGIS_HOME`: `m365-aegis.exe --version` 0, `doctor --offline` 0, `whoami` 3, the
  home not created by them.
- 2026-10-30 — `TokenHygieneTests` green, the marker in no file of the home and in neither stream while the token went
  out as the bearer of all 6 Graph requests; the Pester case green; red on the token in the sign-in result (`stdout`),
  on `Hide-Secret` without its secrets (`stdout`, `stderr`, and the Pester case) and on the log's scrubbing without its
  bearer and JWT patterns (`stdout`, `stderr`, the day's log file), green again after each revert.
- 2026-10-30 — after `origin/main` (with #87, the writes follow-ups) was merged into the branch, the same matrix on the
  merged tree, all exit 0: 932 tests, 0 failed, 0 skipped, 19 s; `check-fixtures.py` 320 files, 0 findings; the
  changelog check; `Invoke-Pester -Path tests/sidecar` 51 passed; PSScriptAnalyzer 0 findings; `doctor --offline` 0
  and `whoami` 3 over an empty `M365AEGIS_HOME`.
- 2026-10-30 — after `origin/main` was merged again (with #88, the window), the same matrix on the merged tree, all
  exit 0: 937 tests, 0 failed, 0 skipped, 17 s; `check-fixtures.py` 322 files, 0 findings; the changelog check;
  `Invoke-Pester -Path tests/sidecar` 51 passed; PSScriptAnalyzer 0 findings; `doctor --offline` 0 and `whoami` 3
  over an empty `M365AEGIS_HOME`. CI on the head before this merge: every check green, `windows` included.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
