# m365-aegis

A thin Microsoft 365 operations layer for Windows 11: a .NET 10 core, the `m365-aegis` command line and the
`m365-aegis-gui` window (a WinForms form with the WebView2 control, a React front end), published
framework-dependent into one folder.
One sign-in through the Windows account manager serves Microsoft Graph (a direct client) and Exchange Online (a
long-lived `pwsh` sidecar running `ExchangeOnlineManagement` with a cmdlet allow-list). Three domains: groups,
Teams and SharePoint; Exchange Online mailboxes; audit, sign-ins and reports. Every write goes through a plan and
needs `--yes` or a confirmation in the window.

## Layout
- `src/M365Aegis.Core` (auth, the Graph client, the Exchange backends and the sidecar script, operations with
  plans, the output contract, the doctor, the bridge protocol), `src/M365Aegis.Cli` (the `m365-aegis` exe),
  `src/M365Aegis.Gui` (the `m365-aegis-gui` exe: the window behind `IUiHost`, the bridge to the Core),
  `gui/web` (the front end: Vite, React, TypeScript, Tailwind, shadcn/ui; built into `dist/` and shipped as
  `wwwroot/`), `tests/` (xUnit for Core and Cli, Pester for the sidecar, the bridge contract samples, the
  scrubbed fixtures)
- `docs/` (origin, research, development, git workflow, maintainer sessions), `tasks/`, the community files at
  the root and under `.github/`; the full layout is in `docs/development.md`

## Commands
```powershell
dotnet build -c Release                                 # the three projects; builds the front end (npm) for the window
dotnet build -c Release -p:SkipFrontendBuild=true       # without the front end
dotnet test -c Release
src\M365Aegis.Cli\bin\Release\net10.0\m365-aegis.exe doctor --offline
npm run dev --prefix gui/web                            # the front end alone, in a browser with the mock transport
```
Before a commit: the checks in CONTRIBUTING.md, "Checks before a commit" (more of them when the packages, the
lock files, `gui/web` or `.github/` change); a change that closes a task regenerates `CHANGELOG.md`.

## Data outside the repo (never commit)
- The tenant's data never enters the repo: no tenant name or id, client id, user principal name, mailbox
  address, object id, sign-in record, token or certificate, and no exact size figure of the tenant either (a count
  of its users, groups, mailboxes, sites or rows is written rounded to an order of magnitude; timings and per-item
  costs may stay); fixtures are scrubbed to `contoso.com` and `00000000-0000-0000-0000-0000000000NN` ids
  (`tests/fixtures/README.md`, `scripts/check-fixtures.py` holds it)
- The tenant profiles (`config.json`), the MSAL cache, the WebView2 data folder, the logs and the plan records live
  under `%LOCALAPPDATA%\m365-aegis\` (D-005; `M365AEGIS_HOME` overrides it, the tests point it at a temp folder);
  the maintainer's tenant, app registration ids and local paths stay in the gitignored `CLAUDE.local.md`
- No home-folder path in any committed file (`C:\Users\<name>\…`)

## Conventions
- Project conventions (language, names, docs to update) are in CONTRIBUTING.md; a new NuGet package follows its
  "Dependencies" section: the one-line reason is a comment in `Directory.Packages.props` (a front-end package's
  reason goes to `docs/development.md`, "The front end"); the license must be on `licenses/allowed.json`
- Every answer of the command line is an envelope or a table from `M365Aegis.Core.Output` (D-006); exit codes
  are the `ExitCode` enum and never change meaning; every write is a `Plan` that `--dry-run` prints and `--yes`
  or a window confirmation applies
- The Exchange Online sidecar runs only the cmdlets on its allow-list (D-004); the undocumented
  `adminapi/beta/InvokeCommand` endpoint is never called
- Git: branches, pull requests and commit rules follow @docs/git-workflow.md

## Tasks
- `tasks/` follows the project-pal task standard (`tasks/README.md`): approved plan → `/tasks promote` (or
  `/tasks add` for later), closing proposes every follow-up and files the approved ones with `/tasks add`,
  `/tasks check` before a task commit; a task file states decisions and results, not conversations; a decision
  stays `proposed` until the maintainer decides it
- A task file's `plan:` field is `none` (a plan file lives outside the repo), and no file carries a home folder
  path, so the repo can go public without a rewrite
- `scripts/sync-issues.py` mirrors every numbered task and decision into a GitHub issue, one way (task 069): edit
  the file, never the issue; a task file's H1 and Context are the issue text

## Memory

- The Claude memory of the maintainer's sessions lives in `.claude/memory/`, next to the code and outside git: the
  folder is ignored, and the committed `.claude/settings.json` points `autoMemoryDirectory` at this checkout's
  absolute path (another machine falls back to its slug memory until a `.claude/settings.local.json` points at its
  own folder). The files are Claude Code's own (`MEMORY.md` index + one `name`/`description`/`type` file per fact)
  under the project-pal memory standard in its ignored mode, validated by `memory_check` (`/memory check`), never
  generated. Business truth goes to the hub, a generic tooling lesson to the setup repo's known-issues doc, a
  decision to `decisions/` (`/tasks decide`) — a decision-shaped memory note graduates there.
