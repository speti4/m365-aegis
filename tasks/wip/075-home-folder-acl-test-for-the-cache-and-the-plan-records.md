---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 075 — A home folder ACL test: the MSAL cache, the profiles and the plan records readable by the user only

## Context

A Windows-only test that creates the home folder through the Core (`AegisHome`), writes the MSAL cache, a
`config.json` and a plan record through the product's own paths, and reads each file's ACL: no access entry for
`Everyone`, `Users` or `Authenticated Users`, the owner and `SYSTEM`/`Administrators` only; the cache's DPAPI
protection asserted by reading the file as bytes and finding no token marker in clear.
Why: SECURITY.md names a cache readable by another user as a vulnerability; `%LOCALAPPDATA%` inherits a user-only
ACL on a standard machine, but a folder created elsewhere through `M365AEGIS_HOME` does not.
Done when: the test is green on the `windows` job and red when the home folder is created with an inherited
`Users: read` entry.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. If the test shows that `AegisHome` does not set the ACL on a folder created
under `M365AEGIS_HOME`, the fix (the folder created with an explicit user-only ACL) is part of this task and the
docs name the rule.

## Docs that move

- `docs/development.md`, the home folder section: the ACL rule (what the product sets, what the test holds)
- `SECURITY.md` only if its scope sentence changes (else none)

## Not in this change

- The MSAL cache's own encryption (DPAPI through `Microsoft.Identity.Client.Extensions.Msal`, asserted, not changed)
- A Linux or macOS home folder mode

## Steps

1. [x] The Windows-only test in the Core tests (skipped on Linux with a reason), the home created through the
       product under a temp `M365AEGIS_HOME`, the three files written through the product's paths, the ACL read
       with `System.Security.AccessControl`
2. [x] The DPAPI assertion over the cache bytes with a marker token
3. [x] The red proof: a home created with an inherited `Users: read` entry fails the test (a Log line)
4. [x] The fix in `AegisHome` if the product does not set the ACL; the docs rule

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)
- 2026-10-30 — the finding: `AegisHome` only resolved the path; each writer created its folder with
  `Directory.CreateDirectory` (`ProfileStore`, `MsalTokenBroker` for the cache, `LogSink`, `PlanStore.Begin`), and the
  window let WebView2 create its data folder, so nothing set an access list and a home under `M365AEGIS_HOME`
  inherited its parent's. The fix is part of this task.
- 2026-10-30 — the fix: `AegisHome.Create(home)` creates a missing home and returns its full path; on Windows through
  `DirectoryInfo.Create(DirectorySecurity)` with `AegisHome.UserOnly()`: access rule protection on (nothing
  inherited), full control for the current user, SYSTEM and Administrators with container and object inheritance,
  the owner the current user. An existing folder is left as it is; off Windows a plain `CreateDirectory`. Called by
  `ProfileStore.Write`, `LogSink.Write` and `PlanStore.Begin` (for the parent of `logs\` and `plans\`), by the cache's
  new `MsalTokenBroker.CreateCacheAsync(home)` (the `StorageCreationPropertiesBuilder` part of the application's
  build, split out so the test writes the cache through it), and by the window's `Program.Main` before the host starts.
- 2026-10-30 — `tests/M365Aegis.Core.Tests/Config/HomeFolderAclTests.cs` (the class `[SupportedOSPlatform("windows")]`,
  each test `Assert.SkipUnless(OperatingSystem.IsWindows(), …)` with the reason): a parent under the temp folder whose
  list adds `Users` and `Authenticated Users` `ReadAndExecute` for everything below; a home under it written through
  `MsalTokenBroker.CreateCacheAsync` + `SaveUnencryptedTokenCache` (a cache whose access token carries a marker),
  `ProfileStore.Save`, `PlanStore.Begin` and `LogSink.Write`; the home and every entry below it (the cache, its lock
  file while it exists, `config.json`, `plans\` and the record, `logs\` and the day's file) hold allow entries for the
  user, SYSTEM and Administrators only and are owned by the user (or Administrators, for an elevated token); the cache
  file's bytes hold the marker neither as UTF-8 nor as UTF-16 while `LoadUnencryptedTokenCache` returns it.
  `TheParentOfTheTestLetsUsersRead` proves the parent would leak (a plain child folder inherits `Users`), and
  `AnExistingHomeIsLeftAsItIs` pins the rule for a folder that is there already. 3 tests, 0.2 s.
- 2026-10-30 — red proof (not committed): `AegisHome.Create` with a plain `Directory.CreateDirectory(full)` instead of
  the protected list: `TheHomeAndEverythingTheProductWritesUnderItAreTheUsersOwn` failed with `…\home grants
  NT AUTHORITY\Authenticated Users: ReadAndExecute, Synchronize (inherited), BUILTIN\Users: ReadAndExecute,
  Synchronize (inherited)` (the names printed localized on this machine); restored, the 3 green.
- 2026-10-30 — docs: `docs/development.md` "The safety tests", the home folder paragraph (the rule: what the product
  sets, when, and what the test holds); `README.md`, the bundle's section "Safety tests and the home folder's access
  list". `SECURITY.md`'s scope sentence ("a cache readable by another user") stands as it is.

## Deviations from the plan

- The home folder's rule is in "The safety tests", not in the section on the home folder: the round's rule keeps each
  parallel bundle's text under its own heading; the paragraph names the rule in full.
- A home that exists already keeps its access list: it may be a folder the user chose and shares on purpose, and
  changing an existing folder's list on every run would be a surprise; a home this rule created stays protected. A
  doctor warning for an existing home that others may read is proposed as a follow-up.
- The window's `Program.Main` creates the home before the host starts (one line), because WebView2 would otherwise be
  the first to create it, without the list.
- `MsalTokenBroker.CreateCacheAsync` was split out of the application's build so the test writes the cache through
  the product's own storage settings (the file name, DPAPI) without a sign-in.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (905 tests, 0 failed, 0 skipped on Windows; 808 before the bundle), `python -I
  scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (318 files, 0 findings); `Invoke-Pester -Path
  tests/sidecar` 46 passed; PSScriptAnalyzer over the sidecar, its tests and `scripts` 0 findings; the `windows` job's
  command line steps over an empty `M365AEGIS_HOME`: `m365-aegis.exe --version` 0, `doctor --offline` 0, `whoami` 3, the
  home not created by them.
- 2026-10-30 — `HomeFolderAclTests` 3 green on Windows (skipped elsewhere with the reason); red without the protected
  list (the inherited `Users` and `Authenticated Users` entries), green again after the revert. The window built with
  its front end, `m365-aegis-gui --smoke` over a fresh `M365AEGIS_HOME`: `ready after 675 ms`, exit 0, the home created
  with a protected list of three entries (SYSTEM, Administrators, the user; none inherited).
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
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
