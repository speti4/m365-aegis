---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 014 — Spike: Photino.NET 4.0.16 on .NET 10 against a WinForms and WebView2 host, recorded as a decision

## Context

A timeboxed comparison (two days) behind `IUiHost`: the scaffold's Photino host against a WinForms window hosting
`Microsoft.Web.WebView2` with the same React front end and bridge; ES modules from a `file://` page (Chromium
refuses them: a custom scheme or a virtual host mapping), the dev loop with `--dev-url` and hot reload, the bridge
on an http origin, the WebView2 data folder under the home, the window handle for the broker (task 013), the
size of the publish and the maintenance state of each. The pick becomes a decision (D-009, written by the main
session from the maintainer's choice) and the loser's host is deleted.
Why: Photino's upstream has been dormant since 2025-01 (D-002 names the risk); the window of M4 should stand on a
host that is maintained.
Done when: both hosts pass the `--smoke` run and show the hello page, the decision file states the pick with the
measurements, and `IUiHost` has one implementation again.
Flow (main session's decision of 2026-10-29): the task session builds the second host, measures both, writes the
measurements and a recommendation into the Log, pushes the branch and stops; the maintainer picks; the main
session sends the pick back and files D-009 on `main`; the task session then deletes the loser, closes and opens
the pull request. The task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The front end": the host that stays, the dev loop, the data folder, the bridge origin
- `Directory.Packages.props`: the WebView2 package's one-line reason while both hosts exist; the loser's package
  removed with it (and `licenses/allowed.json` if the license is new)
- `tasks/decisions/D-002-…`: a dated note pointing at D-009 (the main session writes it with the decision)

## Not in this change

- the decision file itself (D-009, the main session's)
- the first real page of the window (task 017) and the task cards (task 018)
- the broker's parent window for the GUI sign-in beyond measuring that the handle is available

## Steps

1. [x] `Host/WinFormsWebView2Host.cs` behind `IUiHost`, picked by `--host photino|winforms` or `M365AEGIS_GUI_HOST`
       (Photino stays the default while both exist); the GUI project on `net10.0-windows` with WinForms and
       `EnableWindowsTargeting`; the `--smoke` run prints the host, the time to `ready` and the window handle
2. [x] the measurements: ES modules from the shipped `wwwroot`, `--dev-url` hot reload, the bridge on the http
       origin, the data folder under the home, the window handle for the broker, the publish size
       (`scripts/release.ps1 -DryRun`), the Linux `dotnet` job, the upstreams' maintenance state with dates
3. [x] the Log with the numbers and the recommendation; the branch pushed; the main session told — stop here
4. [x] after the pick: the loser deleted (host, package, lock file, license override, docs), `IUiHost` with one
       implementation, `docs/development.md` ("The front end"), the checks, the close

## Log

- 2026-10-29 — promoted by the main session; a task session of its own
- 2026-10-29 — the second host: `Host/WinFormsWebView2Host.cs`, a WinForms `Form` with the `WebView2` control of
  `Microsoft.Web.WebView2` 1.0.4258.31 (BSD-3-Clause, shipped as a license file, so `licenses/overrides.json` names
  it; 1.0.4314.35 of 2026-10-23 was inside the one-week cooldown Dependabot keeps). `CoreWebView2Environment.CreateAsync`
  with `<home>\webview2` as the user data folder, `EnsureCoreWebView2Async`, the shipped `wwwroot` mapped to
  `https://m365-aegis.example/` by `SetVirtualHostNameToFolderMapping` (access kind `Deny`), Photino's own bridge
  script injected by `AddScriptToExecuteOnDocumentCreatedAsync` (so `gui/web` did not change), `WebMessageReceived`
  and `PostWebMessageAsString`; a message is taken only from the page's own origin, a navigation off it is cancelled,
  a new window is refused, the developer tools and the context menu are on only with `--dev-url`. `IUiHost` gained
  `Name` and `WindowHandle`. The GUI project moved to `net10.0-windows` with `UseWindowsForms` and
  `EnableWindowsTargeting`; the Core and the Cli stay `net10.0`; the GUI's output folder is now
  `bin\Release\net10.0-windows\` (the `windows` job's smoke path and CONTRIBUTING follow). The WebView2 package
  references its WPF control on every .NET 5+ project, which made the build report an MSB3277 `WindowsBase` conflict;
  a five-line target drops that reference (nothing WPF ships).
- 2026-10-29 — the measurements, on the maintainer's machine (Windows 11 over Remote Desktop, WebView2 Evergreen
  runtime 156.0.4314.50, SDK 10.0.403), each host started from the Release build with `M365AEGIS_HOME` on a temp
  folder:

  | Criterion | Photino.NET 4.0.16 | WinForms + WebView2 1.0.4258.31 |
  |---|---|---|
  | ES modules of the shipped `wwwroot` | load from `file://` only because Photino starts WebView2 with `--allow-file-access-from-files` (`FileSystemAccessEnabled` defaults to true); with it off the page never sent `ready` (killed after 12 s); `RegisterCustomSchemeHandler("app")` left the page on `about:blank` with the handler never called (a short try, not pursued) | load from `https://m365-aegis.example/index.html` (a virtual host, an https origin), no browser flag |
  | `--smoke`, first run (empty data folder) | exit 0, `ready` after 416 ms | exit 0, `ready` after 1047 ms |
  | `--smoke`, warm, 5 runs | `ready` after 364–382 ms; the exe exits at ~470 ms, its browser processes are gone ~250 ms later | `ready` after 527–543 ms; the exe exits at ~640 ms, but the WebView2 browser processes outlive it by ~6 s, so `Start-Process -Wait` (the CI smoke step) takes 6.4–6.8 s; disposing the control or forcing a GC before the exit did not shorten it |
  | The hello page | shown (screenshot; DevTools protocol: origin `file://`, `#root` with the product and version 0.1.0) | shown (origin `https://m365-aegis.example`, the same text) |
  | `--dev-url` and hot reload | `--smoke` against Vite on `http://localhost:5714`: exit 0, `ready` after 526 ms; an edit of `App.tsx` reached the open window by HMR (Vite logged `hmr update`, the screenshot showed the new heading, the same process) | the same: `ready` after 585 ms, the edit shown by HMR |
  | The bridge on an http origin | works; Photino injects `window.external` on every origin and its handler gets the bare string, no origin to check | works; the host checks `e.Source` against the page's origin and keeps navigation on it |
  | The data folder | `SetTemporaryFilesPath` → `<home>\webview2\EBWebView` (20.0 MB after the runs) | `CreateAsync(null, <home>\webview2)` → `<home>\webview2\EBWebView` (20.7 MB) |
  | The window handle for the broker | `WindowHandle`, readable once `WindowCreated` fired (inside `WaitForClose`); a live top-level window (`GetAncestor(GA_ROOT)` is itself) on every run | `Form.Handle` at `HandleCreated`, before WebView2 starts; top-level on every run |
  | Publish (`release.ps1 -DryRun`) | `main` with Photino alone (the CI run of 2026-10-29): 8.0 MB unpacked, 3.4 MB zip; Photino's files 482 KB (207 KB zipped) plus its own `WebView2Loader.dll` 1.0.2903.40 (162 KB) | both hosts on this branch: 10.1 MB unpacked, 3.9 MB zip, 39 s, no duplicate-file error (the newer loader wins); the WebView2 files 2073 KB (552 KB zipped): `Microsoft.Web.WebView2.Core.dll` 1180 KB after ReadyToRun, the WinForms control 64 KB, the loader 1.0.4258.31 190 KB, two XML doc files 639 KB; WinForms alone ≈ 9.6 MB / 3.7 MB, ≈ 9.0 MB without the XML files (derived from the file sizes) |
  | Runtime on the user's machine | `Microsoft.NETCore.App` 10 | `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` 10 (the ".NET Desktop Runtime"; the GUI's runtimeconfig names both, the Cli still needs only the first) |
  | The Linux `dotnet` job | unchanged | `EnableWindowsTargeting` lets ubuntu restore the Windows Desktop targeting pack: the job's four steps (locked restore, format, the build with `SkipFrontendBuild=true`, 80 tests) green in `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.403) over a copy of this worktree |
  | Logging | Photino.NET prints every call to stdout by default, each `SendWebMessage` payload included (an answer with tenant data would land in a redirected stdout; `SetLogVerbosity(0)` stops it) | prints nothing |
  | Upstream | Photino.NET 4.0.16 and Photino.Native 4.0.22 released 2025-01-23, the last code commit the same day (21 months); one README commit on 2026-03-26 announces Copilot-driven triage for "evolving time constraints"; 44 + 28 open issues; the library ships `net8.0` and `net9.0` builds only; its loader is from the WebView2 SDK of 2024-12 | WebView2 SDK: a stable release about every four weeks (2026: 02-16, 03-16, 04-13, 05-11, 06-11, 07-07, 08-03, 08-28, 09-28, 10-23); the runtime is Evergreen, serviced with Edge. WinForms ships in .NET 10 (LTS, released 2025-11, supported to 2028-11); `dotnet/winforms` commits daily (last 2026-10-28); dark mode and async forms left preview in .NET 10 |
  | Code | `PhotinoHost.cs`, 50 lines | `WinFormsWebView2Host.cs`, 150 lines (the origin checks, the virtual host, the settings), and the five-line MSBuild target |

- 2026-10-29 — recommendation: **WinForms with WebView2.** It stands on two maintained Microsoft components (a
  monthly SDK and the LTS desktop stack) instead of a library with no release in 21 months; it serves the shipped
  page from an https origin without the `--allow-file-access-from-files` flag that Photino's `file://` loading needs
  (a flag that lets any `file://` document read local files); it reaches the whole `CoreWebView2` API (the message's
  origin, the navigation guard, the browser process events, later downloads, printing and drag and drop) where
  Photino passes a bare string; and its window handle exists before the page starts, which is what the broker's
  parent window needs. The costs: about 1.6 MB more unpacked and 0.3 MB more zipped (1.0 MB without the XML doc
  files), the .NET Desktop Runtime as the prerequisite instead of the base runtime (the doctor and the README have to
  say so), a `net10.0-windows` GUI project (the Linux job keeps building it), a window host tied to Windows (the
  front end and the bridge stay portable behind `IUiHost`), about 0.15 s more to the first `ready`, and a CI smoke
  step about 6 s longer while the browser processes wind down. Photino's advantages (a smaller, simpler host and a
  quicker start) do not outweigh a dormant upstream under the window M4 builds on.
- 2026-10-29 — the local checks on this branch: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  the build without and with the front end (0 warnings), 80 tests, `changelog.py --check`, `check-fixtures.py`,
  `check-vulnerable.py` (0), `nuget-license` with the overrides (no finding), `npm-licenses.py` (20 packages, 0
  outside the allowlist), `uvx zizmor .github` (no finding), the Cli's `--version`, `doctor --offline` (0) and
  `whoami` (3), the GUI smoke as the `windows` job runs it (Photino, exit 0) and with `M365AEGIS_GUI_HOST=winforms`
  (exit 0), and `release.ps1 -DryRun` (exit 0)
- 2026-10-29 — the maintainer picked WinForms with WebView2; the main session files it as D-009. Removed:
  `Host/PhotinoHost.cs`, the `Photino.NET` package (`Directory.Packages.props`, the GUI's reference, its lock
  entries through one `dotnet restore`), the `--host` switch and `M365AEGIS_GUI_HOST`, and `IUiHost.Name`; `IUiHost`
  has one implementation and keeps `WindowHandle` for the broker's parent window (task 017). The front end's
  transport lost its Photino names (`HostExternal`, `HostTransport`); it still uses the `window.external` script the
  host injects. `licenses/allowed.json` keeps `Apache-2.0`: without it the license check fails on the xunit packages
  and the front end's lock file holds Apache-2.0 packages too. `docs/development.md` (the layout and "The front
  end": the host, the https virtual host, `--dev-url`, the data folder, the handle, the targeting and the Desktop
  Runtime, the lingering browser processes; the WebView2 license override under CI) and the README (a
  "Requirements" section: the .NET Runtime 10 for the command line, the .NET Desktop Runtime 10 for the window)
  follow.

## Deviations from the plan

- The GUI's output folder moved to `net10.0-windows`, so the `windows` job's smoke path in
  `.github/workflows/ci.yml` and the window line in CONTRIBUTING changed with it.
- `Microsoft.Web.WebView2` is pinned to 1.0.4258.31, not the newest 1.0.4314.35, which was six days old.
- `IUiHost` gained `Name` and `WindowHandle`, and the smoke run prints them (`Host/WindowCheck.cs` tells whether the
  handle is a top-level window), so the broker's parent window is measured in CI too.

## Verification

- 2026-10-29 — on the single-host branch (WinForms + WebView2 only): `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes`, the build without and with the front end (0 warnings), 80 tests; `npm ci`, `lint`,
  `typecheck`, `test` (6) and `build` of `gui/web`; `changelog.py --check`, `check-fixtures.py`,
  `check-vulnerable.py` (0), `nuget-license` with the `deps` job's options and the overrides (no finding),
  `npm-licenses.py` (20 packages, 0 outside), `npm audit --omit=dev` (0), `uvx zizmor .github` (no finding); the
  `dotnet` job's four steps green in `mcr.microsoft.com/dotnet/sdk:10.0` over a copy of the worktree; the Cli's
  `--version`, `doctor --offline` (0) and `whoami` (3); the GUI smoke exactly as the `windows` job runs it
  (`Start-Process … bin/Release/net10.0-windows/m365-aegis-gui.exe --smoke -Wait`), three runs: exit 0, `ready`
  after 562–706 ms, the window handle top-level, 6.6–6.8 s per step; `release.ps1 -DryRun`: exit 0, **9.6 MB
  unpacked, 3.7 MB zip** (Photino on `main`: 8.0 MB and 3.4 MB)
- 2026-10-29 — `tasks_check` without `write`: only the stale README index (the main session's)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change (the D-002 note is the main session's, with D-009)
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: YYYY-MM-DD`
