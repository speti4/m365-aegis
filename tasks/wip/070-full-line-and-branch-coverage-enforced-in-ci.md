---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 070 — 100% line and branch coverage of the Core, the command line, the front end and the sidecar, enforced in CI

## Context

Coverage measured in every test layer and held at 100% lines and branches by a CI threshold: `coverlet.MTP` (in
`Directory.Packages.props`, referenced by no test project yet) for Core and Cli, `@vitest/coverage-v8` for the
front end, Pester `CodeCoverage` for the sidecar; an in-process entry for the command line and a dot-sourceable
function layer for the sidecar, since both run as child processes today and no collector sees them; the tests
that close the gaps (14 source files no test names, `WriteRunner` to `DeviceCodePrompts`; 4 untested front-end
modules).
Why: the maintainer's target of 2026-10-30; a public repo whose writes touch a tenant should show every branch
exercised, and a threshold keeps a new branch from landing untested.
Done when: the three coverage jobs fail CI below 100/100, the reports are run artifacts, and the exclusion list
(the WinForms host and P/Invoke shells that need a display, `[ExcludeFromCodeCoverage]` with a reason each) is
in `docs/development.md`, reviewed by the maintainer.
Part of the coverage and CI bundle (070, 068) of the sixth round of 2026-10-30; the task session owns the steps
below and may reshape them. Decided 2026-10-30: this bundle merges last in the round, after the safety tests
(071–075), the writes follow-ups (064–066) and the window task (063) are on `main`, so the threshold is measured
over the final tree: before its pull request the session merges `origin/main`, moves the new child-process tests
of the safety bundle to the in-process entry and closes the gaps the feature bundles opened. The exclusion list
goes to the maintainer before the pull request.
Decided 2026-10-30 (the maintainer, at checkpoint 1): the exclusion list is accepted as reported (20 members in 7
files, each reason in `docs/development.md`, "Coverage"), and so is the deviation from the plan: no in-process entry
for the command line, `CliTests.RunWith` stays a child process, since coverlet already counts the child processes.

## Docs that move

- `docs/development.md`: a "Coverage" section (the three collectors, the thresholds, the artifacts, the exclusion
  list with a reason each, how to run them locally); "The front end" names the new dev package and its reason;
  "CI and releases" follows the job changes
- `CONTRIBUTING.md`, "Checks before a commit": the coverage runs
- `tests/M365Aegis.Cli.Tests/M365Aegis.Cli.Tests.csproj`: the header comment follows the in-process entry
- `licenses/allowed.json` only if the new package's license is not on it

## Not in this change

- A coverage badge or a third-party coverage service
- Tests of the WinForms host itself (`WinFormsWebView2Host`, the P/Invoke shells): excluded with a reason
- The `windows` job's own coverage run (the Linux `dotnet` job measures Core and Cli; the Windows-only tests are
  excluded or run there without a threshold, the session decides and documents it)

## Steps

1. [x] `coverlet.MTP` in both test projects, the lock files; `scripts/check-coverage.py` merges the two projects'
       JSON reports and fails below 100% of the lines or the branches of an assembly; the `dotnet` job's gate,
       job summary and artifact. The Cli tests stay child processes (see Deviations)
2. [x] The sidecar loadable in process (`-In`/`-Out`/`-Log`, `-Library`), every Pester case run as a child and in
       process, `tests/sidecar/run-with-coverage.ps1` with its threshold in the `sidecar` job and the artifact
3. [x] `@vitest/coverage-v8` (and `happy-dom`) with the 100% thresholds in `vite.config.ts`, `npm run
       test:coverage` in the `frontend` job and the artifact
4. [x] The gap tests: the Core and the Cli to 100% on Windows and on Linux, the front end's modules;
       `[ExcludeFromCodeCoverage]` with a reason each; the exclusion list reported to the main session (checkpoint 1)
5. [x] 068 in the same branch (its own file)
6. [x] After the other bundles merged: `git merge origin/main`, the gaps closed again, the full local matrix, the
       pull request

## Log

- 2026-10-30 — promoted by the main session into the coverage and CI bundle (070, 068)
- 2026-10-30 — the first measure (Windows and Linux, the SDK container) showed coverlet.MTP counting the Cli
  tests' child processes: `m365-aegis.dll` 1796/1872 lines with `Program.Main` covered. The child loads the
  instrumented assembly next to the tests and inherits coverlet's environment variables, and its hits reach the
  report at exit, on both systems. The in-process entry the plan named was there to make the collector see the
  command line, so it was not built (Deviations). Baseline: Core 4519/4947 lines (91.35%), 2249/2734 branches
  (82.26%); Cli 1796/1872 (95.94%), 354/480 (73.75%); the front end about 52% lines over 57 tests; the sidecar
  unmeasured. Linux differed from Windows in `DoctorEnvironment` only.
- 2026-10-30 — `scripts/check-coverage.py` (60acec6): merges coverlet's JSON reports per method, line and branch
  (covered when either test project ran it), prints the files with a gap and their lines, appends a table to the
  job summary, exits 1 below the thresholds (100/100 by default).
- 2026-10-30 — the front end (6d8ff8c): 10 new test files and two helpers (`src/test/dom.ts` over React's `act`,
  `src/test/scripted-transport.ts`), 111 tests (57 before), 527/527 statements, 560/560 branches, 213/213 functions,
  509/509 lines; one source change (`task-card-sketch.tsx` computes the picks once; the second `?? []` could not
  run); `biome.json` ignores `coverage/`.
- 2026-10-30 — the sidecar (80e54a6): the script takes `-In`, `-Out`, `-Log` and `-Library`; the auto-loading
  guard is set in the global scope (pwsh reads it only there; under `-File` the script already ran there, in
  process a script-scope value let the machine's real module load, the task 050 failure); 108 Pester tests (45
  before), 264/264 commands. The fake module's `disconnect-fails-token` reaches the failed disconnect.
- 2026-10-30 — the command line, the sign-in and the doctor (0a319c6): `TableViewTests` (every view over made-up
  results), `CliEdgeTests` (the output mode, the device-code line, the CSV input, the resume refusals of another
  profile and of arguments that no longer parse), `MsalTokenBrokerTests` (the exception mapping, the result, the
  window thread). Source: `Program.PickMode` (the terminal default testable), `WriteRunner.GraphAsync` removed (no
  caller), `WriteRunner.ReadInput` and `ResumeAction.DeleteQuietly` split out, two unreachable `??` and a `for`
  condition simplified, `CallTableView.Columns` and `Cell` without the branches no JSON reaches, the probe's
  `HttpClient` per call; `Doctor.FindPwsh(path, fileName)` pure, `DoctorEnvironment.RunProcess` and `ReadIfExists`
  testable, `LogSink.UseVerbose` (an `AsyncLocal` sink for a test, the process's own sink unchanged).
- 2026-10-30 — Graph, groups, operations, audit, the output, the config and the bridge (afb1bb9): eight new test
  files; `ProductInfo.PlainVersion`, `AegisHome.ResolveFrom`, `BridgeDispatcher.Answer` split out; an unreachable
  `?? "{}"` in the fixture transport and a cached-delegate branch in `PlanResume.Narrow` removed; the window's keep-warm
  log line reads the renewal's own answer.
- 2026-10-30 — the Exchange Online layers (311c67b): four new test files (90 tests), among them a scripted pwsh child
  for the client side of the protocol and `SidecarRunsAlone`, a collection without parallelism for the write to an
  ended child (beside other tests on Windows the write once did not fail). Source: `SidecarStartInfo.ExitTimeout`
  (internal, 10 s as before), `SidecarScript.ReadResource`, `ExchangeConnection.StartAsync(script)`;
  `SidecarClient.Answer` reads non-object data as empty (it threw before); `AdminApiConnection.Create` over the
  token source (its own copies of the fixture and test-organization checks could only be reached through the
  process environment); unreachable `??` and casts in `MailboxReads`, `ExchangeProbes` and `SidecarClient`
  simplified; the return inside `ConnectMeasurement.RunOnceAsync`'s `await using` moved after it.
- 2026-10-30 — CI (13f9401): the `dotnet` job runs `dotnet test --coverlet` and the gate, the `frontend` job `npm
  run test:coverage`, the `sidecar` job `run-with-coverage.ps1`; each uploads its report with
  `actions/upload-artifact` v7.0.2 (pinned by SHA) under `if: !cancelled()`; `uvx zizmor .github` clean.
- 2026-10-30 — the exclusion list (`[ExcludeFromCodeCoverage(Justification = …)]`, 20 members in 7 files), in
  `docs/development.md`, "Coverage", for the maintainer's review: the MSAL calls of `MsalTokenBroker` (9 members),
  `ConsoleWindow.RootOwner`, `BridgeServices.ForWindow`, `Doctor.PwshFileName`, the machine readers of
  `DoctorEnvironment` (5 members, the endpoint probe among them), `SidecarClient.KillQuietly` and `AwaitQuietly`,
  `DeviceCodePrompts.OnStderr`. Docs: `docs/development.md` ("Coverage", "The front end", "CI and releases"),
  `CONTRIBUTING.md` (the three coverage runs in "Checks before a commit"), `README.md` (its own heading),
  `Directory.Packages.props` (coverlet's reason), the Cli test project's header comment.
- 2026-10-30 — checkpoint 2: `git merge origin/main` at 56ba68e (#87 the writes follow-ups, #88 the window, #89 the
  safety tests) as 3839b8a; two conflicts: `README.md` (each bundle's heading kept, `main`'s first) and
  `tests/sidecar/sidecar.Tests.ps1` (the owners' Describe of task 064 kept and run in both modes like the others).
  `WriteRunner.GraphAsync` stays removed: nothing on `main` calls it. The merged tree measured Core 5004/5017
  lines, 2755/2766 branches on Windows (Linux: 4987 and 2750, the Windows-only home creation the other way round),
  the command line 1894/1894 and 466/466, the front end 2 tests failing on the access card's new layer column,
  the sidecar 266/266 commands (120 Pester tests, the safety bundle's token case among them).
- 2026-10-30 — the gaps closed: `AegisHome.Create` keeps the existence check and hands a missing folder to
  `CreateNew` (the Windows access list or a plain folder, excluded with `UserOnly` as one side per operating
  system), `AegisHome.CreateAbove` replaces the `Path.GetDirectoryName(Folder) ?? Folder` of `LogSink` and
  `PlanStore`, `MsalTokenBroker.CreateCacheAsync` excluded (MSAL's cache, one side per system);
  `BridgeDispatcher.ExchangeAsync` describes the layers of an answer with its data only (`where T : class`), the
  null branch no lookup reached gone; tests for the owners of a distribution group without `ManagedBy` and with
  names no recipient resolves, a mailbox access under verbose with and without layers, a record without its start
  time, a record the file system will not remove (`PlanPrune.Remove` internal), a pending change matched by its
  text and one of another action (`PlanResume.Continued` internal); the access card's test expects the layer
  column.
- 2026-10-30 — the write to an ended sidecar (`SidecarWriteTests`) failed once in an uninstrumented full run: the
  write succeeded and the read met the end of the stream. Whether the write fails depends on what else holds the
  pipe's read end, so the write moved into `SidecarClient.WriteQuietlyAsync` (its catch excluded, as the kill's and
  the stderr pump's are), the read reports the stop either way, and the message is
  `the Exchange Online sidecar stopped while answering '<type>'` without the pipe's text; the test asserts it
  exactly and no longer needs the `SidecarRunsAlone` collection, which is gone.

## Deviations from the plan

- No in-process entry for the command line (accepted by the maintainer, 2026-10-30): coverlet.MTP already counts
  the child processes (Log), so the Cli tests keep the child-process harness and `CliTests.RunWith` keeps its signature. The safety bundle's new tests
  over it are measured as they are, and the process-global state the entry would have exposed (the environment,
  the console, `LogSink.Verbose`) stays per process. What a child cannot reach (the terminal default) moved into
  pure functions tested in process.
- The sidecar stays one file (the Core embeds it and the doctor hash-checks it) instead of a separate function
  layer: parameters and `-Library` make it loadable in process.
- A second front-end dev package, `happy-dom`, for the component tests (no DOM was there).
- The Gui exe has no test project and is not measured; its WinForms host carries no `[ExcludeFromCodeCoverage]`
  (nothing measures that assembly). The window's logic is the Core's `Bridge`, which is.
- Four more exclusions after the merge (24 members in 8 files): `AegisHome.CreateNew` and `UserOnly`,
  `MsalTokenBroker.CreateCacheAsync`, `SidecarClient.WriteQuietlyAsync`; the maintainer reviews them in the pull
  request (the first 20 were accepted at checkpoint 1).
- The `windows` job runs the tests without coverage: the platform-only code is excluded, and the gate passes on a
  Windows machine too (checked locally).

## Verification

- 2026-10-30 — checkpoint 1, in the worktree over the tree before the round's other bundles: `dotnet restore
  --locked-mode`, `dotnet format --verify-no-changes --no-restore`, the build without the front end and the full
  build with it, `dotnet test --coverlet` 1126 tests green and the gate at Core 4812/4812 lines and 2676/2676
  branches, Cli 1864/1864 and 464/464 on Windows, the same numbers on Linux (the .NET 10 SDK container, 1126
  green); the front end's lint, type check, `npm run test:coverage` (111 tests, 100/100/100/100) and build;
  `run-with-coverage.ps1` 108 Pester tests, 264/264 commands, PSScriptAnalyzer clean; `check-fixtures.py`, the
  changelog check, `uvx zizmor .github` clean; the `windows` job's steps: `doctor --offline` exit 0, `whoami` exit 3
  in an empty home, `m365-aegis-gui.exe --smoke` exit 0, `scripts/release.ps1 -DryRun` exit 0 (11.4 MB, 4.6 MB
  zipped).
- 2026-10-30 — GitHub, `workflow_dispatch` run 41092363648 on the branch (started by the main session): `changes`,
  `lint`, `workflow-lint`, `deps`, `frontend`, `sidecar` and `dotnet` green (`windows` skipped on a manual run of
  a branch, as the `if:` says); the `dotnet` gate at Core 4812/4812 lines and 2676/2676 branches, Cli 1864/1864
  and 464/464 over 1126 tests; the sidecar 108 tests and 264/264 commands; the three artifacts `coverage-dotnet`,
  `coverage-frontend` and `coverage-sidecar` uploaded.
- 2026-10-30 — checkpoint 2, over the merge of `main` at 56ba68e: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes`, the full build, `dotnet test --coverlet` 1263 tests green and the gate at Core 4992/4992
  lines and 2754/2754 branches, the command line 1894/1894 and 466/466 on Windows, the same on Linux (the SDK
  container, 1260 green, the 3 tests of the home folder's access list skipped there as written); the front end's
  lint, type check, `npm run test:coverage` (117 tests; 535/535 statements, 572/572 branches, 215/215 functions,
  517/517 lines) and build; `run-with-coverage.ps1` 120 tests, 266/266 commands; PSScriptAnalyzer 0 findings;
  the changelog, fixture (323 files), vulnerability and commit-rule checks; nuget-license, `npm-licenses.py`, `npm
  audit --omit=dev`; `uvx zizmor .github`; `doctor --offline` exit 0, `whoami` exit 3 in an empty home, `--smoke`
  exit 0, `release.ps1 -DryRun` exit 0.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
