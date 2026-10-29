# Contributing to m365-aegis

Thank you for taking the time. m365-aegis is a small project with a few firm rules: the tenant's data stays in
the tenant (nothing from it enters the repo), every write to the tenant goes through a plan and a confirmation,
the shipped artifact stays one folder with two exes and nothing to install beyond the .NET runtime and
PowerShell 7.6, and the task files are the plan. Everything below follows from these.

## Start with an issue

Open an issue before a pull request: a [bug report](https://github.com/speti4/m365-aegis/issues/new?template=bug-report.yml)
or a [feature request](https://github.com/speti4/m365-aegis/issues/new?template=feature-request.yml). A short
exchange about the approach saves a rewrite. Security problems go to a private report instead
([SECURITY.md](SECURITY.md)).

## Tenant data never enters the repo

Nothing from a tenant enters the repo: no tenant name or id, no client id, no user principal name, mailbox
address or object id, no sign-in record, no token, no certificate, no screenshot of a real admin center page.
Reproduce a problem with made-up names: `contoso.onmicrosoft.com`, `alice@contoso.com`, ids of the form
`00000000-0000-0000-0000-0000000000NN`. The fixtures under `tests/fixtures/` are recorded answers scrubbed to
those names, and `scripts/check-fixtures.py` fails on anything else (`tests/fixtures/README.md` has the rules).
The tenant profiles, the MSAL cache, the logs and the plan records live under `%LOCALAPPDATA%\m365-aegis\`,
never under the repo; `.gitignore` keeps `config.json`, `msal-cache*.bin` and certificates out.

## Build prerequisites

On Windows 11 you need:

- The .NET 10 SDK, in the version `global.json` names (`winget install Microsoft.DotNet.SDK.10`); the build
  rolls forward within the feature band.
- Node 24 (`.node-version`; `winget install OpenJS.NodeJS.LTS`) for the front end under `gui/web`.
- PowerShell 7.6 or later (`winget install Microsoft.PowerShell`) and the `ExchangeOnlineManagement` module 3.10
  or later (`Install-Module ExchangeOnlineManagement -Scope CurrentUser`) for the Exchange Online sidecar; the
  tests run against a fake module and need only pwsh. Pester 5.7 (`scripts/pester-version.txt`) for the sidecar
  tests: `Install-Module Pester -RequiredVersion 5.7.1 -Scope CurrentUser -Force -SkipPublisherCheck`.
- Python 3 for the helper scripts, and `uv` for zizmor (`winget install astral-sh.uv`).

```powershell
dotnet build -c Release                                      # the three projects; runs npm ci and the Vite build for the window
dotnet build -c Release -p:SkipFrontendBuild=true            # without the front end
dotnet test -c Release
src\M365Aegis.Cli\bin\Release\net10.0\m365-aegis.exe doctor --offline --table
src\M365Aegis.Gui\bin\Release\net10.0-windows\m365-aegis-gui.exe   # the window
npm run dev --prefix gui/web                                 # the front end alone, at http://localhost:5173 with a mock host
```

The front end is built into `gui/web/dist` and copied next to the window's exe as `wwwroot/`; `dist/` is never
committed. The layout, the CLI, the release and the helper scripts are described in
[docs/development.md](docs/development.md).

## Branches, pull requests and commits

Work on a branch and open a pull request; it is squash-merged once CI is green, so its title and description
become the commit message. Commit subjects and pull request titles are imperative and start with a type in
lower case, `type: subject` or `type(scope): subject`, the type one of `feat`, `fix`, `docs`, `ci`, `chore`,
`tasks` and `deps`; a `Merge …` or `Revert "…"` subject passes as git writes it. The hook below checks this. The
full rules are in [docs/git-workflow.md](docs/git-workflow.md). Enable the hook once per clone:

```powershell
git config core.hooksPath .githooks
```

## Checks before a commit

The same checks as CI, job by job ([docs/development.md](docs/development.md#ci-and-releases)). Every change:

```powershell
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet build --no-restore -c Release -p:SkipFrontendBuild=true
dotnet test --no-build -c Release
python -I scripts/changelog.py --check
python -I scripts/check-fixtures.py
```

`--locked-mode` fails when a `packages.lock.json` is out of step with its project instead of rewriting it (a
package change runs `dotnet restore` once without it and commits the lock files). The changelog check wants
`CHANGELOG.md` to match `tasks/done/` exactly, so a change that closes a task runs `python -I
scripts/changelog.py` and commits the result with it. A snapshot test that fails writes a `*.received.*` file
next to its `*.verified.*` under a test project's `Snapshots/`; when the new rendering is the intended one,
rename the received file to `.verified.` and commit it ([docs/development.md](docs/development.md#the-output-contract)).
The commit messages and the pull request text follow the commit rules, which the hook above checks locally and
the `commit-rules` job again in CI.

When `gui/web/` changes, also:

```powershell
npm ci --prefix gui/web
npm run lint --prefix gui/web          # Biome: lint and format
npm run typecheck --prefix gui/web
npm test --prefix gui/web              # Vitest, the bridge contract samples included
npm run build --prefix gui/web
```

The Core and Cli tests start the Exchange Online sidecar as a child pwsh against the fake module too, so
`dotnet test` needs pwsh on `PATH`. When the sidecar script or its tests change
(`src/M365Aegis.Core/Exchange/Sidecar/`, `tests/sidecar/`), also the Pester tests, which drive the script on its own
and never touch a tenant:

```powershell
Invoke-Pester -Path tests/sidecar
```

When `Directory.Packages.props`, a lock file, `gui/web/package.json`, `.config/dotnet-tools.json` or `.github/`
change, also:

```powershell
python -I scripts/check-vulnerable.py                                   # the NuGet advisories
dotnet tool restore
dotnet nuget-license -i M365Aegis.slnx -t -a licenses/allowed.json -ignore licenses/ignored.json -file-mapping licenses/license-texts.json -exclude-projects "*.Tests" -err
python -I scripts/npm-licenses.py                                       # the front end's packages
npm audit --omit=dev --prefix gui/web
uvx zizmor .github
```

CI (`.github/workflows/ci.yml` and `commit-rules.yml`) runs all of them on every push to `main` and every pull
request; the one Windows job runs on pull requests only. A change in behavior comes with a test: C# tests under
`tests/M365Aegis.Core.Tests` and `tests/M365Aegis.Cli.Tests` (the latter run the built exe as a child process),
front-end tests next to the module they test, sidecar tests under `tests/sidecar`. Tests never touch the network
or a tenant: Graph is tested against the fixtures through the client's transport seam, Exchange Online against
the fake module.

## Dependencies

The shipped artifact is one folder with two exes, published framework-dependent (decision D-007 in
`tasks/decisions/`): the .NET 10 runtime, PowerShell 7.6 and the Exchange Online module are the user's to
install, and `m365-aegis doctor` says which one is missing. A new NuGet package is fine when it earns its place.
Add its version to `Directory.Packages.props` with a one-line comment saying why it is needed and why the ones
already there do not cover it; its license has to be on `licenses/allowed.json`, which the `deps` job checks over
the whole dependency tree. A front-end package follows the same rule, with its one-line reason in
[docs/development.md](docs/development.md#the-front-end) (a `package.json` carries no comments); the shadcn/ui
components under `gui/web/src/components/ui` are generated source and are committed as the CLI writes them.
A Microsoft SDK that pulls hundreds of generated types needs a strong reason over the direct client.

## Tasks and decisions

The plan lives in `tasks/`: one file per item under `backlog/`, `wip/`, `done/` and `dropped/`, decisions
under `decisions/`, milestones in `tasks/roadmap.md`, and a generated index in `tasks/README.md`. The
maintainer turns an accepted issue into a task file.

## Conventions

- C# 14 on .NET 10, nullable and implicit usings on, the analyzers at the recommended level, every warning an
  error; `dotnet format` is clean. TypeScript strict, Biome clean.
- Code, comments, commit messages, docs and UI strings are in English with American spelling (license,
  behavior, color); Microsoft's product names are written as Microsoft writes them.
- File, folder and branch names are kebab-case and descriptive, without ambiguous abbreviations; .NET projects
  and namespaces are `M365Aegis.<Part>`.
- Every answer of the command line is an envelope or a table (D-006): a new command never prints on its own, and a
  new failure condition that no exit code covers gets a new number, never a reused one.
- Commit subjects are imperative and start with a type: `feat:`, `fix:`, `docs:`, `ci:`, `chore:`, `tasks:`,
  `deps:` (dependabot), or the same with a scope, `fix(core):`.

## License

m365-aegis is MIT-licensed ([LICENSE](LICENSE)). By contributing, you agree that your contribution is licensed
under the same terms.
