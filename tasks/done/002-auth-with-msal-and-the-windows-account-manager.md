---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 002 — Auth: MSAL.NET with the Windows account manager, interactive and device-code fallback, `login`, `logout`, `whoami`

## Context

`Auth` in the core signs in as a public client of our own app registration (D-003) through MSAL.NET and the
broker, in the order task 013 measured (D-003, note of 2026-10-29): silent with `OperatingSystemAccount`, silent
with the profile's account chosen by user principal name among the broker's accounts, interactive through the
broker with the console's root owner as the parent window, device code when the broker answers
`window_handle_required`. The cache in a DPAPI-protected file under the home folder, shared by the command line
and the window; per-resource tokens for Graph and `outlook.office365.com`; `login`, `logout` and `whoami` through
the output contract; the registration documented without real values; the tenant profile in `config.json`.
Why: everything else signs in through this; the broker is why the stack is .NET (D-002).
Done when: `whoami` answers silently on the maintainer's machine, the device-code path answers in a redirected
shell, the tests cover the cache and profile logic offline with a fake token source, and no token reaches a log.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the delegated scopes task 002 settles, the shape of
  `config.json` (the tenant profiles), the fallback order and the registration's redirect, without real values.
- `README.md`: the first-run steps (`login`, `whoami`) once they exist.
- `tests/fixtures/README.md`: the `auth/` fixtures in the layout.
- `docs/development.md`, "CI and releases": the license override of the broker's native package.
- `CHANGELOG.md`: regenerated at the close.

## Not in this change

- The Graph client itself (task 003): this task hands out tokens, it calls Graph only for `whoami`'s `/me` if at all.
- The Exchange token hand-off to the sidecar (task 009, task 015) and the Admin API backend (task 010).
- The app-only certificate profile for the Teams chat export (M5, D-003).
- The window's sign-in state and the bridge (task 017); the window only shares the cache file.
- Any change to the app registration beyond what the sign-in needs.

## Steps

1. [x] Packages: the three MSAL references in the Core project, `dotnet restore`, the lock files; the license
       override for the broker's native package (`licenses/overrides.json`, the allowlist, the `deps` job's flag)
2. [x] `Config/TenantProfile.cs`: the profile record and `ProfileStore` over `config.json` (`default`, `profiles`
       keyed by name), with tests
3. [x] `Auth/Resource.cs`, `ITokenBroker` with `BrokerException`, `SignInFlow` (the four-step order, the UPN
       match, the fallbacks, the `Auth` failures with hints), with `FakeTokenBroker` tests
4. [x] `Auth/MsalTokenBroker.cs` and `ConsoleWindow.cs`: the one MSAL adapter (the builder, the broker options,
       the parent window, the file cache, the exception mapping, the cache-only fallback of the silent step)
5. [x] `AuthResults`, `AuthService` (`login`, `whoami`, `logout`, `GetAccessTokenAsync`); a test that no token
       reaches a serialized result
6. [x] Cli: `CommandRunner.RunAsync`, the recursive `--profile`, `login`, `whoami`, `logout` with their table views,
       the device-code prompt on stderr, the doctor's hint text, UTF-8 output; the scrubbed fixtures under
       `auth/`, the snapshots, the child-process tests, the `whoami` line of the windows CI job
7. [x] Docs: `docs/development.md` (the sign-in section, the license sentence), `README.md` (first run),
       `tests/fixtures/README.md`
8. [x] Signed-in round on the maintainer's machine: `login` (silent through the broker, the picker was not
       needed), `whoami` silent and timed, `logout`, `login --device-code` with every stream redirected, a second
       `login` silent (from the cache)
9. [x] The checks of CONTRIBUTING.md, the fixture check and the anonymizer scan, the changelog, `tasks_check`,
       the close; the pull request with its follow-ups

## Log

- 2026-10-29 — promoted by the main session; the plan approved the same day (`.default` per resource, the
  profile written by `login`'s flags, no `profile` command yet); the MSAL surface verified against the 4.91.1
  packages and the library's source before coding: the broker package works from plain `net10.0` (nothing native
  loads off Windows), `window_handle_required` is a string literal of the broker package with no `MsalError`
  constant, a dismissed picker is `MsalError.AuthenticationCanceledError`, device code is not intercepted by the
  broker, `RemoveAsync` also signs the account out of the Windows account manager for this app
- 2026-10-29 — built in the worktree: `Config/ProfileStore`, `Auth/` (the broker seam, the flow, the MSAL adapter,
  the results, the service), `login`/`whoami`/`logout` with a two-column table, the async command runner, the
  recursive `--profile`; 80 tests green on Windows (the flow over a scripted broker, the profile store, the
  service, the snapshots of both renderings over `tests/fixtures/auth`, the child-process cases); the transitive
  `Microsoft.Identity.Client.NativeInterop` ships its license as a file (Microsoft Software License Terms), so
  `licenses/overrides.json` names it and the allowlist carries `LicenseRef-Microsoft-Software-License-Terms`;
  `dotnet nuget-license` green locally with the flag the `deps` job now passes
- 2026-10-29 — the signed-in round, first half, on the maintainer's machine over Remote Desktop
  (workplace-registered, no primary refresh token): `login --tenant … --client-id … --account …` wrote
  `config.json` and signed in **silently** through the broker in 2.7 s (the admin account was in the broker's list
  from the spike of task 013; the operating system account failed first, as expected); `whoami` 1.2 s then 0.7 s,
  both tokens from the broker; `logout` removed one account record in 0.5 s; `whoami` afterwards exit 3 with the
  login hint in 0.6 s (the broker still lists the account, the silent call for it fails). Two findings fixed on
  the spot: the broker returns the scopes without the resource prefix and with `openid`, `profile`, `email`,
  `.default`, so the result types show the permissions filtered (`Scopes.Permissions`); and the exe wrote the
  console's legacy code page into a pipe under a shell started with one (an accented display name arrived
  mangled), so `Program` sets the output encoding to UTF-8 (D-006: accented names stay readable)
- 2026-10-29 — the signed-in round, second half: `login --device-code` with stdin, stdout and stderr all
  redirected printed the device-code sentence on stderr and answered ok in 273 s including the maintainer's
  browser round, both tokens from the identity provider (the second one through the cache's refresh token). The
  next `whoami` then **failed** with exit 3: with the broker enabled, MSAL answers a silent call for an account
  the broker lists from the broker alone and never reaches the refresh token the file cache holds from the
  device-code sign-in, so a sign-in from a redirected shell would have served that one run only. Fixed in the
  adapter: when the broker's silent call for the account fails, the same account is tried through a second
  client over the same cache file without the broker; `whoami` then answered from the cache in 0.3 s (0.4–0.5 s
  wall clock), twice. The order in `docs/development.md` names the step; D-003's note on what the file cache
  holds stays true for a broker sign-in and gains nothing a task session may write

## Deviations from the plan

- The silent step gained a cache-only fallback in the MSAL adapter (a second client over the same cache file
  without the broker), which the plan did not foresee: without it a device-code sign-in served one run only.
- The command line sets its output encoding to UTF-8; the plan had no line on it, the signed-in round showed
  the need (an accented display name mangled in a pipe under a legacy code page).
- The broker signed the admin account in silently on the first `login`, so the account picker was not
  exercised in this round; task 013 measured it on the same machine, and the flow's picker step is covered by
  the scripted tests.

## Verification

- 2026-10-29 — `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c Release
  -p:SkipFrontendBuild=true`, `dotnet test -c Release`: 80 tests green on Windows (the Core: the profile store,
  the flow over the scripted broker, the service, the envelope snapshots; the Cli: the table snapshots and the
  child-process cases, `whoami` without a profile exit 3 among them); `python -I scripts/check-fixtures.py` 24
  files, 0 findings; `dotnet nuget-license … --override-package-information licenses/overrides.json … --error-only`
  exit 0; `python -I scripts/changelog.py --check` green after the regeneration
- 2026-10-29 — against the tenant, on the maintainer's workplace-registered machine over Remote Desktop, from a
  shell with stdout redirected: `login --tenant … --client-id … --account …` ok in 2.7 s, silent, both tokens
  from the broker, `config.json` and `msal-cache.bin` written under the home folder and nowhere else; `whoami`
  1.2 s then 0.7 s; `logout` ok, one account record removed, 0.5 s; `whoami` exit 3 with `run m365-aegis login`
  in 0.6 s; `login --device-code` with stdin, stdout and stderr redirected: the sentence on stderr, ok after the
  browser round; `whoami` and a bare `login` afterwards ok from the cache in 0.3–0.7 s. The raw bytes of a
  redirected `whoami` are UTF-8 (`c5 91` for `ő`). No token, tenant id, client id or user principal name in any
  committed file: a grep over the tree finds them only in the gitignored `CLAUDE.local.md`; the two fixtures
  under `tests/fixtures/auth` were run through the anonymizer scan

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones before the merge
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: YYYY-MM-DD`
