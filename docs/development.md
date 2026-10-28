# m365-aegis — development

How the repo is laid out, how CI and releases work, and what the helper scripts do. Build prerequisites, the
checks before a commit and the project rules are in [CONTRIBUTING.md](../CONTRIBUTING.md).

## Layout

- `src/M365Aegis.Core` — the library: the sign-in (`Auth`, task 002), the Graph client (`Graph`, task 003), the
  Exchange Online backends and the sidecar script (`Exchange`, D-004; `Exchange/Sidecar/aegis-exo-sidecar.ps1`
  travels to every exe's output as `sidecar\`), operations with their plans (`Operations`, M3), the output
  contract (`Output`: the envelope, the exit codes, D-006), the home folder (`Config`), the doctor
  (`Diagnostics`) and the window's message protocol (`Bridge`). Plain `net10.0`, so the tests run on Linux; the
  Windows-only paths sit behind `OperatingSystem.IsWindows()`.
- `src/M365Aegis.Cli` — the `m365-aegis` executable: the commands under `Commands/`, the table writer and the
  output mode under `Output/`. `--json` and `--table` are recursive options; without them a terminal gets the
  table and a pipe the envelope.
- `src/M365Aegis.Gui` — the `m365-aegis-gui` executable: the window behind `Host/IUiHost` (Photino today), the
  bridge dispatch to the Core, `--dev-url` for the Vite dev server and `--smoke` for CI.
- `gui/web` — the front end (see [The front end](#the-front-end)).
- `tests/` — `M365Aegis.Core.Tests` and `M365Aegis.Cli.Tests` (xUnit v3 on Microsoft.Testing.Platform; the Cli
  tests start the built assembly as a child process), `sidecar/` (Pester, with the fake module under
  `sidecar/modules/ExchangeOnlineManagement`), `contracts/bridge/` (the message samples the C# and TypeScript
  tests share), `fixtures/` (recorded, scrubbed answers; `fixtures/README.md`).
- `scripts/` — the helper scripts: the release, the changelog, the commit rules, the fixture check, the
  vulnerability check, the front end's license check, the Pester version pin.
- `docs/` — this page, the git workflow, the maintainer sessions, origin and research.
- `tasks/` — the backlog, the work in flight, the history and the decisions (the task standard).
- `.github/workflows/` — CI, the commit rules, the tagged release and the weekly advisory check;
  `.github/dependabot.yml` — the weekly dependency updates.
- `global.json` — the .NET SDK pin and the test runner; `Directory.Build.props` — the one version and the
  build settings every project shares; `Directory.Packages.props` — every package version with its one-line
  reason; `.config/dotnet-tools.json` — the local tools (nuget-license); `licenses/` — the license allowlist and
  the packages the license check ignores (our own projects); `.editorconfig` — the code style the build enforces.
- `.githooks/` — the `commit-msg` hook over `scripts/check-commit.sh`, enabled per clone with
  `git config core.hooksPath .githooks` ([git-workflow.md](git-workflow.md)).
- `CHANGELOG.md` — every release and the tasks it closed, generated from `tasks/done/` (see
  [The changelog](#the-changelog)).

## The sign-in and the home folder

The Core signs in as a public client of the maintainer's own app registration through MSAL.NET and the Windows
account manager (WAM): silent with the signed-in Entra account on an Entra-joined machine, interactive in the
account picker otherwise, device code where there is no window (D-003; task 002 documents the scopes and the
redirect URI without real values). One sign-in serves Graph and, through `Connect-ExchangeOnline -AccessToken`,
the Exchange Online sidecar. Everything the tool keeps lives under `%LOCALAPPDATA%\m365-aegis\` (D-005):
`config.json` with the tenant profiles, `msal-cache.bin` (DPAPI), `webview2\`, `logs\` (never a token or a
response body) and `plans\` with the plan records. `M365AEGIS_HOME` overrides the folder; the tests point it at
a temporary one.

## The Exchange Online sidecar

Exchange Online is reached in three layers behind one interface (D-004): Graph where the feature exists (message
trace, the usage report, mailbox settings), the Exchange Online Admin API where its six preview endpoints
reach, and otherwise a long-lived `pwsh` child process running `aegis-exo-sidecar.ps1` with the
`ExchangeOnlineManagement` module. The Core and the script talk over stdin and stdout, one JSON object per line
tagged `{"aegis":1,…}`; everything else the process prints is sent to stderr (the script swaps the console's
standard output for its error stream at start-up and keeps the original for the protocol), so a module banner
cannot corrupt the channel. Only the cmdlets on the script's allow-list run. The window keeps the sidecar warm;
the command line starts it on demand. `M365AEGIS_EXO_MODULE_ROOT` makes the script look for the module in one
folder only, which the tests use for the fake module. The doctor checks pwsh 7.6, the module version and the
script's hash.

## The front end

`gui/web` is a Vite app: React 19 with TypeScript, Tailwind 4 through the Vite plugin, shadcn/ui (the `base-nova`
style, components generated into `src/components/ui` and committed as the CLI writes them), Biome for lint and
format, Vitest for the tests. The bridge to the host is `src/bridge/`: the protocol (the same shape as
`M365Aegis.Core.Bridge`, checked against the shared samples under `tests/contracts/bridge`) and the transport,
which uses Photino's `window.external` when the host injected it and a mock in a plain browser, so `npm run dev`
shows the page without the exe. The GUI project's `BuildFrontend` target runs `npm ci` and `npm run build` when
the sources are newer than `dist/index.html` and copies `dist/` next to the exe as `wwwroot/`;
`-p:SkipFrontendBuild=true` skips both. During development, `m365-aegis-gui --dev-url http://localhost:5173`
loads the dev server with hot reload.

The front end's packages, with their reasons (`package.json` carries no comments): `react` and `react-dom`
(the UI); `vite` and `@vitejs/plugin-react` (the build and the dev server); `typescript` (strict types; the 5.x
line while the 7.x native compiler settles); `tailwindcss` and `@tailwindcss/vite` (the utility CSS the shadcn
components are written in); `shadcn`, `@base-ui/react`, `class-variance-authority`, `cn`, `lucide-react`,
`tw-animate-css` and `@fontsource-variable/geist` (what `shadcn init` and its components import: the component
primitives, variant classes, the class-name helper, the icons, the animations, the font); `vitest` (the tests);
`@biomejs/biome` (lint and format in one pinned binary); `@types/*` (the type definitions).

## The output contract

Every answer of the command line is one of two renderings of the same data (D-006): a table for a terminal, or
the JSON envelope `{"ok":…,"data":…,"error":{"code","message","hint"},"meta":{"requestIds","elapsedMs",
"backend"}}` on one line for a pipe or an agent; `--json` and `--table` force one. The exit codes are the
`ExitCode` enum in the Core (0 ok, 2 usage, 3 auth, 4 not found, 5 throttled, 6 partial, 7 refused without
`--yes`, 10 a required doctor check failed) and never change meaning. A write is a plan the command prints
under `--dry-run` and applies under `--yes`; the window shows the same plan and asks for a confirmation.

## CI and releases

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, under a minutes budget (D-005):
the Linux jobs `dotnet` (locked restore, `dotnet format --verify-no-changes`, the build with the analyzers, the
Core and Cli tests), `deps` (the vulnerability script, `nuget-license` against `licenses/allowed.json` over the
shipped projects, `npm audit` and the front end's license script), `frontend` (Biome, the type check, Vitest,
the Vite build), `sidecar` (PSScriptAnalyzer and the Pester tests against the fake module, with the runner's own
pwsh), `lint` (the changelog check, skipped on a tag, see [The changelog](#the-changelog); the fixture check; the
commit-rules self-test) and `workflow-lint` (zizmor over the workflows and `dependabot.yml`; a finding accepted on
purpose carries `# zizmor: ignore[<audit>]` on the step's name line with its reason in the comment above). The
one Windows job, `windows`, runs on pull requests and tags only: the full build with the front end, the tests,
`m365-aegis --version` and `doctor --offline`, the window's `--smoke` run (the page loads and the bridge sends
`ready`), and the release script's dry run. `.github/workflows/commit-rules.yml` runs `commit-rules`
(`scripts/check-commit.sh` over every pushed commit and the pull request text) on the same pushes and pull
requests. `.github/workflows/advisories.yml` restores and audits the lock files every Monday and by hand (`gh
workflow run advisories.yml`), so an advisory for a package already in a lock file shows up between pull
requests; it is not a required check. The workflows run with a read-only token by default; only the release job
may write contents. Every restore runs in locked mode (`RestoreLockedMode` under `CI`), so a lock file out of
step with its project fails instead of being rewritten.

Some of the setup lives in the repository settings, not in a file (task 012, applied 2026-10-29): a ruleset
protects `main` (changes arrive through a pull request with squash merge only, the checks `dotnet`, `deps`,
`frontend`, `sidecar`, `lint`, `workflow-lint`, `windows` and `commit-rules` must pass, the history stays
linear, and force pushes and deletion are blocked; the repository admin may bypass the ruleset for a small
housekeeping commit, see `docs/git-workflow.md`; a job skipped by its `if:` still
satisfies its required check; the checks are not "strict", so a branch need not be rebased on the head of
`main` before the merge, which spares the minutes budget); another ruleset keeps `v*` tags from being moved or
deleted; squash merge is the only merge method, with the pull request title and description as the commit
message, and the branch is deleted on merge; Dependabot alerts and security updates are on. Private
vulnerability reporting (the link in SECURITY.md) and secret scanning are not offered on a private repository
of this plan and are turned on when the repo goes public (M6); until then the fixture check in CI and the
pre-commit checks stand in for secret scanning.

Every action is pinned by commit SHA with the version in a comment. Dependabot proposes the updates for the
NuGet packages, the SDK pin, the front end's packages and the actions once a week, a release only after it is a
week old; a pin is changed through such a pull request, never by retagging by hand. Before a change to the
dependencies or the workflows, the same checks run locally (CONTRIBUTING.md, "Checks before a commit").

### The .NET SDK and the packages

`global.json` pins the SDK's feature band (`rollForward: latestFeature`), so a newer band and its new analyzer
rules cannot turn CI red without a pull request; dependabot's `dotnet-sdk` ecosystem proposes the bump.
`Directory.Packages.props` holds every package version (central package management, transitive pinning on) with
a one-line reason; a project names a package without a version. `Directory.Build.props` turns every warning
into an error, the analyzers to `latest-recommended`, the code style of `.editorconfig` into the build and
`NuGetAudit` on, so a known vulnerability in any package fails the restore. `.config/dotnet-tools.json` pins
nuget-license, restored with `dotnet tool restore`.

### The release

`scripts\release.ps1` publishes `M365Aegis.Cli` and `M365Aegis.Gui` framework-dependent for `win-x64` with
ReadyToRun into one folder (D-007: the .NET 10 runtime is Windows Update's to patch, so a runtime fix needs no
release of ours), checks that `m365-aegis.exe` carries the version of `Directory.Build.props` (the only place it
is written), writes `THIRD-PARTY-NOTICES.md` (nuget-license over the shipped packages in Markdown, then the
front end's packages from the lock file by `scripts/npm-licenses.py`), copies the README as `GUIDE.md`, and
packs everything into `dist\m365-aegis-<version>.zip`: the two exes, their assemblies, `wwwroot\`,
`sidecar\aegis-exo-sidecar.ps1`, the notices and the guide. Next to the zip it writes `SHA256SUMS.txt` (the
zip's SHA-256 in the `sha256sum -c` format) and `release-notes-<version>.md`. `-DryRun` does all of it in a
temporary folder and prints the sizes, which is what the `windows` job runs on every pull request.

```powershell
dotnet tool restore                      # once per clone
scripts\release.ps1                      # dist\: the zip, SHA256SUMS.txt, release-notes-<version>.md
scripts\release.ps1 -DryRun
```

Pushing a tag `v<version>` runs every job; when they pass, the `release` job checks that the tag is on `main`,
runs `release.ps1`, attests the build provenance of the zip and publishes it with the checksum file as a GitHub
release titled `m365-aegis <version>`, with the notes file as its text, through the runner's `gh` CLI. A
downloaded zip is checked against its attestation with `gh attestation verify <zip> --repo speti4/m365-aegis`.
Signed exes are M6.

### The changelog

`CHANGELOG.md` is generated from the task files by `scripts/changelog.py`: every file under `tasks/done/` is a
line (its H1, linked to the file) under its milestone, in the section of the first release tag whose tree holds
it under `tasks/done/`, or under *Unreleased* when no tag holds it yet. The tag trees decide rather than the
`closed` date, because tasks close both before and after a tag on the same day. The summary under a heading is
written by hand and kept by every run; the lists are never edited by hand. A task that changed the project rather
than the product (the repo, CI, the process) carries `changelog: housekeeping` in its frontmatter and is listed
last, under *Project housekeeping*. The release notes are one section of the changelog without that group (one
line counts it and links the changelog), with a footer on the checksum, the attestation and the notices file. The
script reads the task files through the parser of `scripts/sync-issues.py`, which is why that script is in the
repo although the issue mirror is not set up. A release goes like this:

1. In a pull request: bump `<Version>` in `Directory.Build.props`, write the summary under *Unreleased* in
   `CHANGELOG.md`, run `python -I scripts/changelog.py` and merge.
2. Tag the merge commit `v<version>` and push the tag; the release job takes the notes from the changelog (the
   new tag takes the *Unreleased* summary).
3. Afterwards, `python -I scripts/changelog.py` stamps the section with the version and the tag's date; commit
   it with the next change. Until then `--check` reads the newest tag without a section as not there yet and
   prints a notice, so the pull requests opened in between, dependabot's included, stay green.

```powershell
python -I scripts/changelog.py                 # rewrite CHANGELOG.md
python -I scripts/changelog.py --check         # exit 1 when it is out of date
python -I scripts/changelog.py --notes 0.1.0   # one release's notes (a version without a tag: Unreleased)
```

Between releases the committed *Unreleased* list matches `tasks/done/` exactly: the lint job's `--check` fails
on any difference, so the change that closes a task also runs `python -I scripts/changelog.py` and commits the
regenerated `CHANGELOG.md`.

### The other scripts

- `scripts/check-fixtures.py` — every file under `tests/fixtures`, `tests/sidecar` and `tests/contracts` carries
  made-up data only: the allowed domains, the `00000000-…` ids, nothing that looks like a token.
- `scripts/check-vulnerable.py` — `dotnet package list --vulnerable --include-transitive` as JSON, with a failing
  exit code when it finds anything (the command itself exits 0 either way).
- `scripts/npm-licenses.py` — the front end's shipped packages against `licenses/allowed.json`, from the lock
  file; `--notices` appends them to the notices file.
- `scripts/check-commit.sh` — the commit rules of [git-workflow.md](git-workflow.md), with a `--self-test`.
- `scripts/pester-version.txt` — the Pester version the `sidecar` job installs.

## Fixtures

Every test runs offline and against synthetic data: recorded Graph, Admin API and Exchange Online answers
scrubbed to made-up names under `tests/fixtures/` (`fixtures/README.md` has the layout and the rules), the
bridge contract samples under `tests/contracts/bridge/`, and the fake `ExchangeOnlineManagement` module under
`tests/sidecar/modules/`. A signed-in round on the real tenant is a task of its own, and its result is a dated
line in that task's Verification; nothing it printed is committed before it is scrubbed.
