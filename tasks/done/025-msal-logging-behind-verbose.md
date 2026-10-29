---
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 025 — MSAL's non-PII log into `logs\` behind a `--verbose` flag

## Context

Once the command line has a logging path into `%LOCALAPPDATA%\m365-aegis\logs\` (task 021, same branch), the MSAL
adapter registers `WithLogging` with PII logging off and the library's lines go there under `--verbose` only; a
WAM error context may carry a tenant id in a service message, so the sink scrubs GUIDs before writing.
Why: a failed sign-in on a colleague's machine (a Conditional Access verdict, a broker error code) is
undiagnosable from the envelope's one sentence; the library's log names the step and the code.
Done when: a `--verbose login` writes a log file without any token, authorization code or user principal name
(a test over the sink with a scripted line set), and a run without the flag writes nothing.
Bundled with 021, 024 and 008 on one branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the `--verbose` flag and what `logs\` holds
- `README.md` if the first-run section names the flag for a failed sign-in

## Not in this change

- a log of the Graph client's requests (task 003's request ids go into the envelope's meta)
- PII logging, ever

## Steps

1. [x] the recursive `--verbose` option on the root command sets the process's verbose sink (`LogSink.Verbose`,
   the home folder's `logs\`) before any command runs
2. [x] `MsalTokenBroker.WithLog`: `WithLogging(callback, LogLevel.Verbose, enablePiiLogging: false,
   enableDefaultPlatformLogging: false)` when there is a sink, nothing otherwise; the callback drops a line
   marked as carrying PII; the profile's tenant, client id and account are registered with the sink for redaction
3. [x] the scrub in the sink: the registered values, then the patterns (a JWT, a bearer header, the OAuth
   parameters that carry a secret or an identity in a query string or a JSON body, a user principal name, a GUID,
   a `*.onmicrosoft.com` name), with a test over a scripted line set; the MSAL client without a sink has no
   logging callback, with one it has PII logging off (tests over the built client's `AppConfig`)
4. [x] `docs/development.md` names the flag, the folder and what is scrubbed; `README.md` names `--verbose login`
   for a failed sign-in

## Log

- 2026-10-29 — promoted by the main session into the command line bundle (021, 025, 024, 008)
- 2026-10-29 — `WithLogging(LogCallback, LogLevel?, bool? enablePiiLogging, bool? enableDefaultPlatformLogging)`
  and `IAppConfig.LoggingCallback` / `EnablePiiLogging` checked against the MSAL.NET 4.91 reference on Microsoft
  Learn before coding; the PII guide lists what MSAL never logs (tokens, authorization codes) and what it counts
  as PII (the user name, the login hint, the authorization URI), which PII logging off keeps out
- 2026-10-29 — plan: a process-wide sink set once by `Program` rather than a parameter through every command, so
  every MSAL client a command builds (now and in the later tasks) logs under the flag without each command
  passing it; the scrub goes beyond GUIDs because a broker message is free text

## Deviations from the plan

- The sink scrubs more than the GUIDs of the plan: the token, the authorization code and the user principal name
  of the done-when by pattern, the profile's own tenant, client id and account by value, and `*.onmicrosoft.com`
  names. A correlation id is a GUID too and goes with them.
- One log file a day, appended to by every process (shared with task 021's internal errors), instead of a file
  per run; each line carries the process id.

## Verification

- 2026-10-29 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`,
  `dotnet build -c Release` with and without the front end (0 warnings), `dotnet test -c Release` 166 passed, 0
  failed; `python -I scripts/check-fixtures.py` 38 files, 0 findings; `python -I scripts/changelog.py --check` up
  to date; with an empty `M365AEGIS_HOME`: `--version` 0.1.0, `--json doctor --offline` exit 0, `--json whoami`
  exit 3; `scripts/release.ps1 -DryRun` exit 0
- 2026-10-29 — the sink over a scripted line set (a bearer JWT, an authorization code in a redirect, a token
  response body, a device code, the user principal name, the tenant id, the client id, a custom domain and an
  `onmicrosoft.com` name): none reaches the file, the step and the error code stay; the MSAL client without a sink
  has no logging callback, with one PII logging and platform logging are off; a line marked as carrying PII is
  dropped
- 2026-10-29 — on the maintainer's machine (main session, the real home): `--json --verbose login` exit 0 in 0.6 s,
  both tokens from the cache; the day's log has 697 lines of `msal: Info:` and `msal: Verbose:` with the MSAL
  header and the broker's steps readable, the correlation ids as `<guid>`; a search for `eyJ`, both user principal
  names of the machine, the client id, the tenant id, the tenant's domain and an unredacted `code=` found nothing;
  `whoami` without `--verbose` afterwards exit 0 and the log's size unchanged

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
