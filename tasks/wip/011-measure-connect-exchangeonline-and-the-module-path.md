---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 011 — Measure: cold `Connect-ExchangeOnline` with and without `-CommandName`, and the module path under OneDrive

## Context

On the maintainer's machine, the cold connect time of the Exchange Online module in three variants
(default; `-SkipLoadingFormatData`; both with `-CommandName` limited to the sidecar's allow-list), five runs
each, median and spread; and whether `$env:PSModulePath` starts under a OneDrive-synced Documents folder, which
the research names as a module discovery slowdown. The numbers go into `docs/origin.md` next to the Graph
import measurements of 2026-10-21.
Why: the sidecar's warm-up budget and the doctor's advice depend on them; nothing published gives a figure.
Done when: the numbers are in `docs/origin.md` with the date and the module version, and the sidecar's connect
uses the fastest variant that still answers the allow-list.
Part of the Exchange bundle (009, 015, 016, 011, 010) of the round of 2026-10-29: the task session writes the
measurement script, the main session runs it on this machine (ExchangeOnlineManagement 3.10.1, pwsh 7.6.9) and
sends the numbers back.

## Docs that move

- `docs/origin.md`, "Problem": the connect figures next to the Graph import figures of 2026-10-21
- `docs/development.md`, the Exchange Online sidecar: the variant the connect uses and why

## Not in this change

- the doctor's advice on the module path (a follow-up if the OneDrive case is real)

## Steps

1. [x] `m365-aegis probe exchange-measure` (reshaped from a script, Deviations): three variants × five cold runs
       (a fresh pwsh sidecar each, the broker's token), median and spread, the module version, the
       `$env:PSModulePath` OneDrive check; no tenant identifier in the output
2. [x] Live run (main session): the numbers
3. [x] The numbers in `docs/origin.md`; the sidecar's connect uses the fastest variant that answers the allow-list

## Log

- 2026-10-29 — promoted by the main session into the Exchange bundle (009, 015, 016, 011, 010)
- 2026-10-29 — first shape: `scripts/measure-exo-connect.ps1 -UserPrincipalName <upn>`, five fresh pwsh per
  variant with the module's own sign-in
- 2026-10-29 — the live run of that script failed: in each of the 15 fresh processes the module's sign-in
  prompted the maintainer and then stopped with "A window handle must be configured" (the module's broker sign-in
  from a console without a parent window); the imports took about 1.0 to 1.1 s, no connect happened. The script
  was removed: no measurement uses the module's own sign-in
- 2026-10-29 — reshaped into the hidden `m365-aegis probe exchange-measure [--runs 5] [--variants
  default,skip-format-data,command-name]` (`Exchange/ConnectMeasurement.cs`): the broker's Exchange Online token is
  asked for once, silently from the cache, and every run starts a fresh pwsh sidecar and connects it with that
  token over the protocol, the way a command's sidecar connects (`-AccessToken` and `-Organization`, never on a
  command line, never printed or logged), in the variant asked (`connect` takes `skipLoadingFormatData` and
  `commandName` set to false; the default stays `-SkipLoadingFormatData -CommandName <the REST cmdlets of the
  allow-list>`), checks that every allowed cmdlet answers (`checkCommands`), runs `Get-AcceptedDomain` and ends the
  process. Per run: the total to the end of the first cmdlet, the start to `hello`, the module's discovery and
  import, the connect, the first cmdlet; per variant the median and the spread; the pwsh and module versions; the
  OneDrive checks of the module's folder, the child's `PSModulePath` entries and the Documents folder (booleans,
  no path). The figures are with the token hand-off the sidecar uses, so they are the relevant ones. Checked
  against the fake module
- 2026-10-29 — the numbers (medians, the spread in brackets, five fresh sidecars per variant, one silent broker
  token, ExchangeOnlineManagement 3.10.1, pwsh 7.6.9): pwsh to `hello` about 0.45 s in every variant, finding and
  importing the module 1.2 s (1.1 to 1.5 s); the connect 1982 ms (1697-2498) by default, 1049 ms (954-1103) with
  `-SkipLoadingFormatData`, 476 ms (434-528) with `-SkipLoadingFormatData -CommandName`; the first cmdlet 372, 388
  and 299 ms; the total to the first answer 4218 ms (3908-4548), 3180 ms (3054-3629) and 2442 ms (2318-2722). Every
  variant answered the whole allow-list. Neither the module, the module path nor the Documents folder is under
  OneDrive on this machine, so the doctor needs no advice about it. The sidecar keeps `-SkipLoadingFormatData
  -CommandName`, the fastest variant that still answers the allow-list; the numbers are in `docs/origin.md`,
  "Problem", next to the Graph import figures of 2026-10-21

## Deviations from the plan

- A hidden command of the core instead of `scripts/measure-exo-connect.ps1`: the script's module sign-in
  prompted in every fresh process and failed without a parent window; the command measures the token hand-off
  the sidecar uses, with one silent broker token and no prompt.
- The environment block of the failed script run (2026-10-29) stands: pwsh 7.6.9, ExchangeOnlineManagement 3.10.1,
  the module, the first `PSModulePath` entry, every `PSModulePath` entry and the Documents folder not under
  OneDrive.

## Verification

- 2026-10-29 — locally, on Windows, after the rework: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes`, the Release build with the front end (0 warnings), `dotnet test` 405 green (the measurement
  against the fake module among them), `Invoke-Pester -Path tests/sidecar` 23 green, PSScriptAnalyzer 0 findings,
  `changelog.py --check`, `check-fixtures.py` (137 files, 0 findings); `doctor --offline` 0, `whoami` 3 with an
  empty home, the window's `--smoke` 0, `release.ps1 -DryRun` green
- 2026-10-29 — against the tenant, in the main session's signed-in round (around 22:20, UTC+1) from this branch's
  Release build: `probe exchange-measure --runs 5` exit 0 in 55 s, no sign-in prompt, 15 runs, none failed, the
  figures of the note above. The earlier script run with the module's own sign-in prompted and failed in every
  process (Log) and was replaced

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
