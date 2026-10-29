---
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 024 — `profile list`, `profile use` and `profile remove` over the tenant profiles of `config.json`

## Context

A `profile` command group in the command line: `list` prints the profiles with the default marked, `use <name>`
moves the default, `remove <name>` deletes a profile (and signs its account out first). Task 002 writes a profile
only through `login --tenant … --client-id … --account …` and picks one with `--profile`.
Why: a second tenant (a lab tenant, a customer's) or a second account in the same tenant needs the file edited by
hand today; the commands keep the file's shape the Core's.
Done when: the three commands answer through the output contract with tests over a temporary home, and
`docs/development.md`'s sign-in section names them.
Bundled with 021, 025 and 008 on one branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The sign-in and the home folder": the three commands next to the `config.json` shape
- `README.md`: the first-run section, if it mentions a second profile

## Not in this change

- `profile add` (a profile is still written by `login`'s flags)
- an app-only certificate profile (M5, D-003)

## Steps

1. [x] `ProfileStore` gains `Describe` (the profiles, the effective default marked), `Use`, `Remove` and `Find`
   (`src/M365Aegis.Core/Config/TenantProfile.cs`), and the result types `ProfileList`, `ProfileSummary`,
   `ProfileRemoval`; tests
2. [x] `AuthService.RemoveProfileAsync` signs the profile's account out before deleting it, skips the sign-out when
   another profile uses the same app registration and account, and keeps the profile when the sign-out fails
3. [x] `Commands/ProfileCommand.cs`: the three subcommands, two table views, the samples in
   `tests/shared/AuthSamples.cs`, the envelope and table snapshots
4. [x] child-process tests over temporary homes (an empty list, `use` then `list`, an unknown name at a terminal
   and in a pipe, a removal that never reaches the broker, `profile` without a subcommand);
   `docs/development.md` and `README.md` name the commands

## Log

- 2026-10-29 — promoted by the main session into the command line bundle (021, 025, 024, 008)
- 2026-10-29 — plan: the default after a removal is the only profile left, or none when several are left, so
  a command never signs in to a tenant nobody chose; a name the file does not have is `not-found` (exit 4) for
  `use` and `remove`; the sign-out goes through the same code as `logout`
- 2026-10-29 — built; on the way, a hand-edited profile without its tenant, client id or account (or `null`)
  crashed `Resolve` with a NullReferenceException, so loading the file now checks every profile and answers with
  exit 3 and the fix as the hint

## Deviations from the plan

- An unknown name for `use` and `remove` is `not-found` (exit 4); `--profile <unknown>` on the sign-in commands
  stays `auth` (exit 3), as task 002 left it, since there it means "no usable sign-in".
- `remove` skips the sign-out when another profile uses the same app registration and account, which the plan did
  not foresee: signing it out would have signed the remaining profile out too.
- Loading `config.json` checks that every profile has its three values (a crash found on the way).

## Verification

- 2026-10-29 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`,
  `dotnet build -c Release` with and without the front end (0 warnings), `dotnet test -c Release` 166 passed, 0
  failed; `python -I scripts/check-fixtures.py` 38 files, 0 findings; `python -I scripts/changelog.py --check` up
  to date; with an empty `M365AEGIS_HOME`: `--version` 0.1.0, `--json doctor --offline` exit 0, `--json whoami`
  exit 3; `scripts/release.ps1 -DryRun` exit 0
- 2026-10-29 — the store and the service over temporary homes (list, use, remove with the default moving or not, an
  unknown name, a profile without its values, the sign-out first, a shared account kept, a failed sign-out keeping
  the profile) and the child-process tests (an empty list, `use` then `list`, an unknown name in a pipe and at a
  terminal, a removal that never reaches the broker, `profile` without a subcommand)
- 2026-10-29 — on the maintainer's machine (main session) over a copy of the home with only `config.json`: `profile
  list` exit 0, the one profile the default; with a second profile added by hand on the same app registration and
  account, `profile use` moved the default, `list` showed it, `--json profile remove` of it exit 0 with `signedOut:
  false`, the default back on the first profile and the removed one gone from the file; `profile remove` of an
  unknown name exit 4 (`not-found`) with the list hint; the real `whoami` afterwards exit 0. The removal with a
  sign-out was not run against the real account (it would sign the maintainer out); the scripted tests cover it

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
