---
size: S
origin: done/075-home-folder-acl-test-for-the-cache-and-the-plan-records.md
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 078 — The doctor warns when an existing home folder is readable by other users

## Context

A doctor check on Windows reads the home folder's access list and warns when `Everyone`, `Users` or
`Authenticated Users` can read it (the rule of task 075: the product creates a missing home with a user-only list but
leaves an existing folder as it is), with the `icacls` line that fixes it in the hint.
Why: SECURITY.md names a cache readable by another user as a vulnerability; a home created elsewhere through
`M365AEGIS_HOME` before task 075, or by hand, keeps its inherited list, and the doctor is where a user finds out.
Done when: the check is `pass` on a home the product created, `warn` on a temp home with an inherited `Users: read`
entry, `skipped` off Windows, and the docs' check list names it.
Seventh round of 2026-10-30, on its own branch; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, the doctor's check list: the new check, its three outcomes and the hint
- `README.md`, the doctor section, if it lists the checks
- `SECURITY.md`: the doctor check named next to the home folder rule, if the file names the safeguards

## Not in this change

- Fixing the access list from the doctor (the hint carries the `icacls` line, the user runs it)
- The MSAL cache file's own access list beyond the folder's

## Steps

1. [x] A doctor check reads the home folder's access list on Windows and warns when `Everyone`, `Users` or
       `Authenticated Users` has a read right, with the `icacls` fix in the hint; `skipped` off Windows
2. [x] Core tests: `pass` on a home the product created (task 075's helper), `warn` on a temp home with an inherited
       `Users: read` entry, `skipped` off Windows (the platform behind an injectable seam if needed)
3. [x] `doctor --offline` still runs the check (no sign-in needed); snapshots updated where the rendering changed
4. [x] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/078-doctor-home-folder-acl`
- 2026-10-30 — the check: `home-access` in `Doctor.CheckHomeAccess`, right after `home`, not required. Off Windows
  `skipped`; a home that does not exist yet `skipped` (the product creates it user-only); an unreadable list `warn`
  with `icacls "<home>"` as the hint. Otherwise an allow entry for `S-1-1-0`, `S-1-5-32-545` or `S-1-5-11` (by SID,
  so the Windows display language does not matter) whose mask carries `ReadData`/`ListDirectory`, `GENERIC_READ` or
  `GENERIC_ALL` is a reader; any reader makes it `warn` ("Users (inherited), Authenticated Users (inherited) can read
  it: …"), none `pass`. The hint (`Doctor.HomeAccessFix`) is one line that runs the same in pwsh and in cmd and gives
  the folder the list of `AegisHome.UserOnly`: `icacls "<home>" /inheritance:r /grant:r "<user>:(OI)(CI)F"
  "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11` (the combined
  operations tried by hand on a temp folder first: exit 0, the three entries left).
- 2026-10-30 — the seam: `DoctorEnvironment.ReadHomeAccess` returns a `HomeAccess` (the process's account name for
  the hint, and every entry as an `AccessEntry`: SID string, allow, raw mask, inherited), so the check over it runs and
  is measured on Linux; the reader of the real machine, `ReadHomeAccessFromDisk`, is the one excluded member
  (`ThisMachine`, listed in docs/development.md, Coverage).
- 2026-10-30 — tests: `DoctorTests` (any runner) — a user-only list with an `Authenticated Users: AppendData` entry and
  a `Users` deny entry passes; `Everyone` with `GENERIC_READ`, `Users` inherited, `Authenticated Users` with both an
  inherited `GENERIC_ALL` and an explicit entry warns with the exact detail and the exact `icacls` line; skipped off
  Windows and without a home (the reader not called), a warning when the reader returns null; the healthy fake now
  passes 14 checks. `HomeFolderAclTests` (Windows only, the real reader) — `TheDoctorPassesAHomeTheProductCreated`
  (`AegisHome.Create` under the `Users`-readable parent) and
  `TheDoctorWarnsOnAHomeThatInheritsUsersReadAndItsFixLineMakesTheHomeTheUsersOwn` (a plain folder under the same
  parent warns with `Users (inherited)` and `Authenticated Users (inherited)`; the hint run as it is through
  `icacls.exe` exits 0, the check then passes and task 075's `AssertTheUsersOwn` holds).
- 2026-10-30 — the doctor fixtures (`healthy.json` pass, `warnings.json` warn with the hint, `pwsh-missing.json`
  skipped) carry the new row, so the 3 envelope snapshots of the Core and the 4 doctor renderings of the Cli changed
  (the table's `fix home-access: icacls …` line included); accepted as received.
- 2026-10-30 — docs: `docs/development.md` — the doctor's check table row, the seam list, a paragraph on the rule and
  the fix line, a sentence in "The safety tests" home folder paragraph, the coverage exclusion row; `README.md` — the
  doctor sentence and the safety section; `SECURITY.md` — the sign-in and tokens bullet names the user-only home and
  the doctor's warning.
- 2026-10-30 — merged in #97 (squash) after a rebase onto the merged #105 and #98 (the index conflict resolved to
  `main`'s, the rebased tree built and tested locally: 1272 tests, 0 failed); the one follow-up (every foreign account
  judged) approved and filed as task 088; closed on `main` by the main session.

## Deviations from the plan

- The check is named `home-access`, a row of its own after `home`, not required (the tool runs either way).
- A home that does not exist yet is `skipped` too (nothing to read; the product creates it user-only), and an
  unreadable list is a `warn` with `icacls "<home>"` as the hint; a deny entry is not weighed against an allow entry,
  and accounts other than the three groups are not judged.
- The Windows tests live in `HomeFolderAclTests` next to task 075's (its `ReadableParent` and `AssertTheUsersOwn`),
  and also run the hint, which proves the fix line, not only the warning.
- The hint names the user by the process's account name (`MACHINE\name` or `DOMAIN\name`), resolved at run time, so
  it is copied and run as it is; the fixtures use `CONTOSO\alice`.
- The doctor fixtures gained the row, so the snapshots changed although the renderer did not.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (1268 tests, 0 failed, 0 skipped on Windows; 5 of them new), `python -I
  scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (323 files, 0 findings); the coverage gate
  (`dotnet test --coverlet` + `scripts/check-coverage.py`): `M365Aegis.Core.dll` lines 5017/5017, branches 2762/2762,
  `m365-aegis.dll` lines 1894/1894, branches 466/466 (100%).
- 2026-10-30 — the built `m365-aegis.exe doctor --offline --table`, exit 0 each time, with `M365AEGIS_HOME` under a
  new folder at the root of a data drive given `Users` and `Authenticated Users` read: the home missing — `home-access
  skip  no home folder yet; the product creates it readable by the user only`; the home created plainly —
  `home-access  warn  Everyone (inherited), Users (inherited), Authenticated Users (inherited) can read it: every
  account on this machine can read the profiles, the plan records and the logs` (the drive root's own `Everyone`
  entry showed up too) and `fix home-access: icacls "<home>" /inheritance:r /grant:r "<machine>\<user>:(OI)(CI)F"
  "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11`; the hint run in pwsh
  — icacls exit 0; after it — `home-access  pass  neither Everyone, Users nor Authenticated Users can read it`. The
  default home under `%LOCALAPPDATA%` — `pass`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-30` by the main session after the merge
