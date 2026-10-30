---
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M2 Exchange
---

# 050 — The sidecar tests never fall back to the machine's real Exchange Online module

## Context

When the fake module under `tests/sidecar/modules/` lacks a cmdlet of the allow-list, pwsh's module auto-loading
imports the machine's real `ExchangeOnlineManagement` for that one name; the script sets
`$PSModuleAutoLoadingPreference = 'None'` when `M365AEGIS_EXO_MODULE_ROOT` points at a test module, and a test holds
the fake module's exports equal to the allow-list, so a missing export fails the test instead of reaching the
real module.
Why: on 2026-10-30 a new allow-list cmdlet missing from the fake's manifest made a test call the real module (it
failed unconnected and nothing reached a tenant, but the guard was luck, not design).
Done when: a cmdlet removed from the fake's exports fails the sidecar tests with a message naming it, and
`Invoke-Pester -Path tests/sidecar` stays green on a machine without the real module.
Part of the Exchange and window follow-ups bundle (050, 049, 048, 046, 047) of the round of 2026-10-30, its first
task; the task session owns the steps below and may reshape them. The parallel M3 bundle (051–054) adds write
cmdlets to the allow-list and to the fake module's manifest in the same change, so the export test holds after
both merge; the test's message names the missing cmdlet so the other bundle's agent sees it at once.

## Docs that move

- `tests/sidecar/README.md`: the auto-loading guard and the export test
- `docs/development.md`, "Tests and measurements" of the sidecar section: one line on the guard

## Not in this change

- the fake module's data or the failing identities
- the doctor's module check (the real module, unchanged)

## Steps

1. [x] `$PSModuleAutoLoadingPreference = 'None'` in the script under a test module root; the script's embedded copy and hash
2. [x] The Pester test: the fake manifest's exports equal the allow-list, the message names the missing cmdlet
3. [x] `Invoke-Pester -Path tests/sidecar` green with the real module hidden from the session
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window follow-ups bundle (050, 049, 048, 046, 047)
- 2026-10-30 — the guard: with `M365AEGIS_EXO_MODULE_ROOT` set, the script imports `Microsoft.PowerShell.Management`
  and `Microsoft.PowerShell.Utility` (nothing loads them on demand once auto-loading is off) and sets
  `$PSModuleAutoLoadingPreference = 'None'`. The embedded copy is the same file (`LogicalName` in the Core's project),
  so its hash follows the build; nothing else to update.
- 2026-10-30 — two Pester tests: the fake manifest's `FunctionsToExport` equals the allow-list (read from `hello`) plus
  `Connect-ExchangeOnline` and `Disconnect-ExchangeOnline`, the failure naming the missing cmdlet; and a copy of the
  fake without `Get-EXOMailboxFolderStatistics` answers "is not recognized". The copy has to carry another version:
  a loaded module of the same name and version (the fake and the real one are both 3.10.1 here) shadows the real one,
  so the copy with the fake's own version passed without the guard too. With version `0.0.50` and the guard removed,
  the real module answered: `You must call Connect-ExchangeOnline before calling any other cmdlet.` (the case of the
  morning); with the guard, the cmdlet is not recognized.

- 2026-10-30 — closed by the main session on `main` after #23; the approved follow-ups of the pull request are filed as backlog items 055–062

## Deviations from the plan

- the copy in the second test carries another module version than the fake, which is what makes auto-loading reach
  the real module; the task's text assumed any missing export would

## Verification

- 2026-10-30 — `Invoke-Pester -Path tests/sidecar` (Pester 5.7.1): 29 passed, 0 failed; and once more in a pwsh whose
  `PSModulePath` holds only pwsh's own modules and a copy of Pester (`Get-Module -ListAvailable ExchangeOnlineManagement`
  found 0): 29 passed, 0 failed. The second new test without the guard: 1 passed, 1 failed with the real module's
  sentence above.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
