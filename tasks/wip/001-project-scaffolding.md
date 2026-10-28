---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-28
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 001 — Project scaffolding: repo structure, task standard, decisions, origin and research

## Context

Greenfield repo. A research round on 2026-10-28 (three reports: the market, the no-new-app and agent paths, the
stacks) settled the frame before any code: build a thin personal operations layer and not a product (D-001),
.NET 10 with a core, a System.CommandLine command line and a Photino window with a React front end (D-002),
our own public-client app registration with MSAL.NET and the Windows account manager (D-003), Exchange Online in
three layers with a pwsh sidecar and a cmdlet allow-list (D-004), a private repo under a CI minutes budget with
no tenant data in the repo (D-005), one output contract with every write behind a plan (D-006), and a
framework-dependent ReadyToRun publish (D-007). Decided 2026-10-28: the name is `m365-aegis` (the working title
`graphctl` collided with a graph-database CLI on GitHub and misnamed the scope). This task lays down the repo
structure, the task standard, the decisions, the origin and research pages, and a solution that compiles,
tests and passes every CI check; no product code beyond the output contract, the offline doctor, the bridge
protocol and the sidecar script with its tests.

## Docs that move

- `README.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`: written for m365-aegis after
  a proven repo pattern.
- `docs/origin.md`, `docs/research/2026-10-28-m365-admin-tooling-landscape.md`, `docs/development.md`,
  `docs/git-workflow.md`, `docs/maintainer-sessions.md`, `tests/fixtures/README.md`: new.
- `tasks/roadmap.md`, `tasks/decisions/D-001…D-007`: new.
- `CHANGELOG.md`: the Unreleased section.

## Not in this change

- Any product code: the sign-in, the Graph client, the lookups, the sidecar's core side and the mailbox
  commands are tasks 002–008, 015 and 016; the window's first real page is 017.
- The spikes that settle the Exchange sign-in, the Admin API, the broker from a console and the window host
  (009, 010, 013, 014) and the connect-time measurement (011).
- The GitHub repository settings, made by hand after the first push (012).
- Signed exes, the usage guide and the distribution to the team (M6); the issue mirror of the task files.

## Steps

1. [x] Project registered in the maintainer's planning tool
2. [x] `docs/origin.md`, `docs/research/2026-10-28-m365-admin-tooling-landscape.md`
3. [x] `tasks/` standard: roadmap, D-001…D-007, backlog 002–018, this file, generated README
4. [x] Repo files after a proven pattern: community files, docs, scripts, hooks, CI, dependabot, templates
5. [x] The solution: `M365Aegis.Core` (the output contract, the home folder, the offline doctor, the bridge
       protocol, the sidecar script), `M365Aegis.Cli` (`m365-aegis` with `doctor`), `M365Aegis.Gui` (the Photino
       host behind `IUiHost`, the React front end with shadcn/ui), the xUnit, Vitest and Pester tests
6. [ ] Every check green locally: restore, format, build, test, the front end's lint, type check, tests and
       build, the sidecar's Pester tests, the vulnerability and license checks, zizmor, the changelog and
       fixture checks, the commit-rules self-test, `tasks_check`
7. [ ] The private repo `speti4/m365-aegis`, the first push, CI green

## Log

- 2026-10-28 — `m365-aegis` free on crates.io, npm and PyPI, no exact repository on GitHub; the registry card
  created; the research of the day condensed into the landscape page with its sources
- 2026-10-28 — D-001…D-007 decided; backlog 002–018 written from the roadmap
- 2026-10-28 — the solution scaffolded by hand: .NET SDK 10.0.204 pinned with roll-forward in the feature band,
  central package management, lock files, the analyzers at the recommended level with warnings as errors; the
  packages resolved at the versions the research verified (System.CommandLine 2.0.14, Photino.NET 4.0.16,
  xunit.v3 4.0.2); xUnit v3 on Microsoft.Testing.Platform (the `xunit3` template is not in this SDK, the
  projects are hand-written); 22 C# tests, 6 Vitest tests and 9 Pester tests green
- 2026-10-28 — the front end: Vite 8.3.5, React 19.3.1, TypeScript 5.9.3 (the 7.x native compiler left for
  later), Tailwind 4.3.3, Biome 2.6.3; `shadcn init` (4.23.0, the `base-nova` style) generated the button, the
  helper and the theme tokens; the GUI's `BuildFrontend` target runs `npm ci` and the Vite build and copies
  `dist/` as `wwwroot/` (the item glob inside the target needs a qualified metadata reference); `m365-aegis-gui
  --smoke` exits 0 with the page loaded and the bridge's `ready` received
- 2026-10-28 — the sidecar script and its Pester tests against a fake `ExchangeOnlineManagement` module under
  `tests/sidecar/modules`: the protocol owns stdout (the console's standard output is swapped for stderr at
  start-up), `M365AEGIS_EXO_MODULE_ROOT` pins the module folder for the tests, the fake's `Write-Host` noise
  never reaches the channel
- 2026-10-28 — the checks: nuget-license 4.0.18 exits 11 on a license outside the allowlist (verified with a
  narrowed list), `dotnet package list --vulnerable` needs `--project` on this SDK and exits 0 either way (the
  script fails on a finding), zizmor flagged the npm cache of setup-node in a workflow that publishes (the cache
  is off), the Windows job runs on pull requests and tags only (D-005)

## Deviations from the plan

- The .NET SDK on the machine is 10.0.204, not the 10.0.403 the design named; `global.json` pins 10.0.204 with
  `rollForward: latestFeature`, so CI may run a newer feature band of the same major until dependabot bumps the
  pin.
- `Pester` 5.7.1 was installed in the CurrentUser scope for the sidecar tests (the machine had 3.4.0); the
  `ExchangeOnlineManagement` on the machine is 3.9.0, below the 3.10 the doctor wants, and is left for task 011.

## Verification

- (to be written at the close)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
