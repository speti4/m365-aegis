---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 008 — `doctor` with the full check list: the module version, WebView2, the sidecar hash, the sign-in, the endpoints

## Context

The scaffold's offline doctor gains the rest: the `ExchangeOnlineManagement` version through a pwsh call (3.10 or
later), the WebView2 runtime from the registry (`pv` under the EdgeUpdate client key), the sidecar script's
SHA-256 against the one compiled into the core, the sign-in state (the cached account and the token expiry for
Graph and Exchange), the device join state from `dsregcmd /status` (joined, registered or neither, and a primary
refresh token or not, which explains a failed silent operating-system-account sign-in; task 013) and the
reachability of the three endpoints; `--offline` keeps skipping the last one; exit 10 on a required failure.
Why: the first thing a colleague runs when the tool does not work, and the prerequisite check the framework-
dependent publish (D-007) relies on.
Done when: every check has a pass, a warn and a fail case in the tests (with the module and the registry faked),
and the table names what to install for each failure.
Bundled with 021, 025 and 024 on one branch; the task session owns the steps below and may reshape them. The
maintainer's machine has the module at 3.9.0 (a warn case) and is workplace-registered without a primary
refresh token (the explanation line of task 013), which the live check of the main session confirms.

## Docs that move

- `docs/development.md`: the doctor's check list (which section holds it today, or a new one under "The output
  contract"), and the "The Exchange Online sidecar" line on the script's hash
- `README.md`: the first-run section's doctor line, if it lists the checks
- `tests/fixtures/README.md`: the new `doctor/` fixtures

## Not in this change

- the sidecar process itself (task 015) — the doctor only hashes the script and asks pwsh for the module version
- a fix-it mode (the doctor advises, never installs)
- the Exchange sign-in through the sidecar (task 009)

## Steps

1. [x] the checks in `src/M365Aegis.Core/Diagnostics/Doctor.cs` behind `DoctorEnvironment` (the pwsh call, the
   registry, `dsregcmd /status`, the sidecar script's two copies, the sign-in through `AuthService.WhoAmIAsync`,
   the endpoint probe), each replaceable in the tests; the script embedded in the Core
   (`Exchange/SidecarScript.cs`)
2. [x] the classification per check (required or not, pass, warn, fail, skip) and a hint per warning and failure:
   the check's `hint` in the envelope, a `fix <check>: …` line under the table
3. [x] tests for every check over the faked environment, with the pass, warn and fail cases; the made-up
   `dsregcmd` outputs and the `warnings.json` report under `tests/fixtures/doctor/`, `healthy.json` and
   `pwsh-missing.json` brought to the full list; the snapshots; `doctor --offline` in an empty home still exits 0
4. [x] `docs/development.md` (a "The doctor" section with the check list, the sidecar's hash line), `README.md`
   (the first-run section), `tests/fixtures/README.md`

## Log

- 2026-10-29 — promoted by the main session into the command line bundle (021, 025, 024, 008)
- 2026-10-29 — the WebView2 detection checked against Microsoft's distribution guide (the `pv` of the
  `{F3017226-…}` client key under `EdgeUpdate\Clients`, per machine in the 32-bit view and per user; empty or
  `0.0.0.0` is not installed); the `dsregcmd /status` keys (`AzureAdJoined`, `WorkplaceJoined`, `AzureAdPrt`)
  read on the maintainer's machine, values hidden
- 2026-10-29 — plan: one pwsh call answers both the pwsh and the module version (a pwsh start costs most of the
  run); the sidecar's hash compares the copy next to the exe with a copy embedded in the Core, the same guarantee
  as a hash string compiled in without an MSBuild hash step; the three endpoints are probed in parallel with any
  HTTP answer counting as reachable; the sign-in check resolves the profile first, so a missing one never loads
  MSAL
- 2026-10-29 — built and run on the session's machine against a temporary home: `doctor --offline` in 0.9 s,
  exit 0, the module at 3.9.0 a warning with `fix exchange-online-module: Update-Module ExchangeOnlineManagement`,
  the device "registered, not Entra-joined" a warning, the sidecar's hash matching the build, WebView2 passing;
  the online run without a profile reached the three endpoints (HTTP 405 and 417 count as reachable) and warned
  about the missing profile without touching MSAL

## Deviations from the plan

- The module check is not required: a missing module is a `fail` without exit 10, since the Graph domains work
  without it (the cmdlets of the sidecar do not), and the Windows job of CI runs `doctor --offline` expecting 0
  on a runner that need not have the module.
- `--offline` skips the sign-in too, not only the endpoints: a silent sign-in may refresh a token over the
  network, and the scaffold's offline run already skipped it.
- `dotnet-runtime` has a pass case only: it is the runtime the doctor itself runs on.
- The sidecar's hash is compared with a copy of the script embedded in the Core rather than a hash string
  compiled in; a copy that differs in its line endings only is a warning, any other difference a failure.
- The endpoints are required checks (no answer is exit 10), and a registered-only device is a warning, not a
  failure (task 013: the normal case on such a machine).

## Verification

- 2026-10-29 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`,
  `dotnet build -c Release` with and without the front end (0 warnings), `dotnet test -c Release` 166 passed, 0
  failed; `python -I scripts/check-fixtures.py` 38 files, 0 findings; `python -I scripts/changelog.py --check` up
  to date; with an empty `M365AEGIS_HOME`: `--version` 0.1.0, `--json doctor --offline` exit 0, `--json whoami`
  exit 3; `scripts/release.ps1 -DryRun` exit 0
- 2026-10-29 — every check over the faked environment with its pass, warn and fail cases (the module and the
  registry faked, the made-up `dsregcmd` outputs), the report's verdict and exit 10 on a required failure,
  `--offline` never calling the sign-in or the probe; the snapshots of the healthy, the failed and the warnings
  reports in both renderings
- 2026-10-29 — on the maintainer's machine (main session, the real home, online): `doctor` exit 0, healthy, 12 rows
  in 2.9 s cold and 1.2 s warm: pwsh 7.6.9 pass; the module at 3.9.0 a warning with `Update-Module
  ExchangeOnlineManagement` as the fix; the sidecar's hash matching the build; WebView2 pass; the profile pass; the
  device a warning, registered and not Entra-joined, without a primary refresh token (task 013's explanation line);
  the sign-in pass with both token expiries; the three endpoints answering (HTTP 200, 405 and 417, all under 250
  ms)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
