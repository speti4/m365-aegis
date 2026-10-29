# m365-aegis — development

How the repo is laid out, how CI and releases work, and what the helper scripts do. Build prerequisites, the
checks before a commit and the project rules are in [CONTRIBUTING.md](../CONTRIBUTING.md).

## Layout

- `src/M365Aegis.Core` — the library: the sign-in (`Auth`, task 002), the Graph client (`Graph`, task 003), the
  Exchange Online backends and the sidecar script (`Exchange`, D-004; `Exchange/Sidecar/aegis-exo-sidecar.ps1`
  travels to every exe's output as `sidecar\` and is embedded in the Core for the doctor's hash check),
  operations with their plans (`Operations`, M3), the output contract (`Output`: the envelope, the exit codes,
  D-006), the home folder and the tenant profiles (`Config`), the doctor and the log (`Diagnostics`) and the
  window's message protocol (`Bridge`). Plain `net10.0`, so the tests run on Linux; the
  Windows-only paths sit behind `OperatingSystem.IsWindows()`.
- `src/M365Aegis.Cli` — the `m365-aegis` executable: the commands under `Commands/`, each with the table view of
  its result type, and the one rendering path under `Output/` (the command runner, the renderer, the table view
  contract, the table writer, the output mode; see [The output contract](#the-output-contract)). `--json` and
  `--table` are recursive options; without them a terminal gets the table and a pipe the envelope.
- `src/M365Aegis.Gui` — the `m365-aegis-gui` executable: the window behind `Host/IUiHost` (a WinForms form with
  the WebView2 control, D-009), the bridge dispatch to the Core, `--dev-url` for the Vite dev server and `--smoke`
  for CI. The one project on `net10.0-windows` (see [The front end](#the-front-end)).
- `gui/web` — the front end (see [The front end](#the-front-end)).
- `tests/` — `M365Aegis.Core.Tests` and `M365Aegis.Cli.Tests` (xUnit v3 on Microsoft.Testing.Platform; the Cli
  tests start the built assembly as a child process; each project keeps its Verify snapshots under
  `Snapshots/`), `shared/` (the source files both C# test projects link), `sidecar/` (Pester, with the fake
  module under `sidecar/modules/ExchangeOnlineManagement`), `contracts/bridge/` (the message samples the C# and
  TypeScript tests share), `fixtures/` (recorded, scrubbed answers; `fixtures/README.md`; the Graph
  answers are read from the source tree through `shared/GraphFixtures.cs`).
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
account manager (WAM), in the order task 013 measured (D-003): silent as the account Windows is signed in with
(an Entra-joined machine with a primary refresh token; a registered-only machine fails this step in well under a
second, which is the normal case there), silent as the profile's account chosen by user principal name among
the accounts the broker lists (never the first in the list; when the broker cannot answer for it, the same
account is tried through the file cache alone, which is where a device-code sign-in's refresh token lives: with
the broker enabled MSAL never falls back to it on its own, measured 2026-10-29), the account picker through the
broker with the console window's root owner as its parent, and device code when the broker answers
`window_handle_required` (a redirected shell, a scheduled task) or under `login --device-code`. `whoami` and every lookup take the silent
paths only and fail with exit code 3 and the login command as the hint; `login` goes all the way; `logout`
removes the account from the cache and from the broker's record for this app and keeps the profile. The
device-code sentence is printed on stderr, so stdout stays the envelope. `Auth/MsalTokenBroker.cs` in the Core
is the one place MSAL is called; the flow (`Auth/SignInFlow.cs`) and `AuthService` are tested offline against a
scripted broker. One sign-in serves Graph and, through `Connect-ExchangeOnline -AccessToken`, the Exchange
Online sidecar: each resource gets a token of its own with the `.default` scope
(`https://graph.microsoft.com/.default`, `https://outlook.office365.com/.default`), so a token carries every
delegated permission the tenant administrator consented to in the portal, the code names none, and `whoami`
shows what a token actually carries.

The app registration, whose ids stay out of the repo (D-003): a single-tenant public client, the "Mobile and
desktop applications" platform with the broker redirect `ms-appx-web://microsoft.aad.brokerplugin/<application
id>` and `http://localhost`, "Allow public client flows" on, no credential, and admin consent for the delegated
permissions the domains need. The sign-in itself needs `User.Read` (Microsoft Graph) and the Exchange Online
backends `Exchange.Manage` and `Exchange.ManageV2` (Office 365 Exchange Online; tasks 009 and 010); the lookups
of the other domains add theirs as their tasks settle them (the groups, Teams and SharePoint lookups of task 005,
the audit lookups of task 006, `ExchangeMessageTrace.Read.All` for the message trace of task 007), each with a
line here.

- `call` (task 003): nothing of its own; the endpoint it calls needs what its reference page names.
- Groups (task 005): `GroupMember.Read.All` for the groups, their members and owners and `group owners-missing`,
  and `User.ReadBasic.All` for the names of members and owners that are users (without it Graph returns their ids
  and types only).
- Teams (task 005): `Team.ReadBasic.All` for `team list`.
- SharePoint (task 005): `Sites.Read.All` for `site list`; with a delegated token the site search is trimmed to the
  sites the signed-in account can open (`getAllSites`, every site regardless, is application-only).
- Message trace (task 007): `ExchangeMessageTrace.Read.All`, and once per tenant the service principal of
  Microsoft's message trace application (application id `8bd644d1-64a1-4d4b-ae52-2e0cbf64e373`, the same in every
  tenant), which an administrator creates with `Connect-MgGraph -Scopes Application.ReadWrite.All` and
  `New-MgServicePrincipal -AppId 8bd644d1-64a1-4d4b-ae52-2e0cbf64e373`. Provisioning can take several hours; until
  then the service answers 401 naming the service principal, which `trace` reports as exit code 8 with this step
  as the hint. After a new consent, `login` fetches a token that carries it.

Everything the tool keeps lives under `%LOCALAPPDATA%\m365-aegis\` (D-005): `config.json` with the tenant
profiles, `msal-cache.bin` (DPAPI, with a `.lockfile` next to it while a process reads or writes it, so the
command line and the window share one cache), `webview2\`, `logs\` and `plans\` with the plan records.
`M365AEGIS_HOME` overrides the folder; the tests point it at a temporary one.

`logs\` holds one file a day, `m365-aegis-yyyy-MM-dd.log`, appended to by every process (tasks 021 and 025): the
stack trace of an unexpected internal error, always (exit code 70, see [The output contract](#the-output-contract)),
and, only under the recursive `--verbose` option, MSAL's own log of the sign-in (`WithLogging` with PII logging
off, every level, so a broker error code or a Conditional Access verdict names its step). A run without
`--verbose` registers no logging callback and a run without an error writes nothing, not even the folder. Every
entry is scrubbed before it is written (`Diagnostics/LogSink.cs`): the profile's tenant, client id and account,
and anything shaped like a JWT, a bearer header, an OAuth secret or identity parameter (`code=`,
`access_token=`, `login_hint=`, … in a query string or a JSON body), a user principal name, a GUID or a
`*.onmicrosoft.com` name, so no token, authorization code or user principal name reaches the file and a log can be
attached to a bug report as it is. Never a response body either.

`config.json` is written by `login --tenant … --client-id … --account …` (the first profile becomes the default;
`--profile <name>` on any command picks another one), managed by `profile list` (the profiles, the default one
marked with `*`), `profile use <name>` (moves the default) and `profile remove <name>` (signs the profile's account
out first, unless another profile uses the same app registration and account; a failed sign-out keeps the
profile; when the default goes, the only profile left becomes the default, and with several left none does) (task
024), and may be edited by hand. `use` and `remove` with a name the file does not have answer exit code 4
(`not-found`); a profile without its tenant, client id or account is the file's fault, exit code 3 with the fix as
the hint:

```json
{
  "default": "work",
  "profiles": {
    "work": {
      "tenant": "contoso.onmicrosoft.com",
      "clientId": "00000000-0000-0000-0000-000000000010",
      "account": "alice@contoso.com"
    }
  }
}
```

## The Graph client

`Graph/` in the Core is the one way to Microsoft Graph (task 003): `HttpClient` and System.Text.Json, no SDK
(D-002), and everything D-006 keeps out of the commands. A lookup describes a request as a `GraphRequest` (the
method, the path under the version built with `GraphQuery.Path` so every value is escaped, `GraphQuery.Literal`
for an OData string, `Version = GraphVersion.Beta` for the beta endpoint, `EventualConsistency = true` for an
advanced query on directory objects, `Permission` for the delegated permission a 403 hint names) and reads the
answer through `GraphClient`:

- `GetAsync<T>` reads one answer typed; `GetPagesAsync<T>`, `GetAllAsync<T>` (an async stream) and `ListAsync<T>`
  follow `@odata.nextLink` with the same headers to the last page; `SendAsync` returns the answer as it came;
  `BatchAsync` sends up to twenty requests per `$batch` call and returns one `GraphBatchResponse` per request in
  the order given (`EnsureSuccess` turns a failed item into the contract's failure).
- Typed records derive from `GraphObject`: the `@odata.type`, and every field the record does not name kept in
  `AdditionalData` (`JsonExtensionData`), so nothing Graph sent is dropped.
- A 429 or 503 is retried after its `Retry-After` (seconds or a date) plus up to 250 ms of jitter, or, without the
  header, after an exponential back-off with jitter (1, 2, 4, 8 s); four retries per request, and at most 60 s of
  waiting per command, so a `Retry-After` past the budget fails at once with exit code 5 and the wait in the hint.
  A throttled item inside a batch is sent again in a new batch after the longest `Retry-After`.
- A failed answer becomes an `AegisException`: 401 auth (3), 403 forbidden (8, the hint names the permission and
  the consent), 404 not found (4), 429/503 throttled (5), anything else, and no network, a service error (9).
- Every answer's `request-id` goes into the command's `RequestLog`, which the command runner puts into the
  envelope's `meta.requestIds`, a failed command's included.

The token comes from `AuthService.GetAccessTokenAsync` for the Graph resource, silently, on the first request
(`GraphConnection.Create`), and is sent to `https://graph.microsoft.com` only. The transport is a seam,
`IGraphTransport`: `HttpGraphTransport` in the product, `FixtureGraphTransport` in the tests, which answers from
`tests/fixtures/graph` (the URL mirrored as the path, `tests/fixtures/README.md`). `M365AEGIS_GRAPH_FIXTURES`
pointed at such a folder makes the command line itself use the fixtures and ask for no token, which is how the
child-process tests run every Graph command offline; like `M365AEGIS_EXO_MODULE_ROOT`, it only ever makes the
tool read local files. A command in the Cli is one call: `GraphRunner.RunAsync(parsed, json, table, profile,
(graph, ct) => Lookup(graph, …, ct), View.Instance, cancellationToken)`, with the lookup in the Core returning a
`CommandResult<T>`.

`call <METHOD> <path> [--beta] [--body <json|@file>] [--all] [--yes]` is the raw request: the path under the
version (`/me`), with the version in front (`/v1.0/me`, `/beta/me`; a version in the path and `--beta` that
disagree are a usage error) or a full Graph URL; the answer is the envelope's data unchanged, `--all` merges every
page's `value`, and the table shows a collection as a row per item and an object as a field per row. A GET is
sent as it is; any other method is a write and is sent only with `--yes`: without it the request (method, URL,
body) is the data, nothing is sent and the exit code is 7 (D-006).

## The lookups

Each lookup is a static method in the Core over the `GraphClient`, returning a `CommandResult<T>`, and a command
with its table view in the Cli (task 005, task 007):

- `group members|owners <group>` (`Groups/GroupLookups.cs`): the group is an object id (`GET /groups/{id}`), an
  exact display name, or with an `@` an exact address (`$filter` … `eq`, an advanced query), else the one group whose
  name `$search` matches (`"displayName:<words>"`); no match is exit code 4, and more than one is exit code 4 with
  the candidates and their ids in the hint. The members and owners come over every page with `$select`, which on
  these lists is an advanced query (`ConsistencyLevel: eventual`, `$count=true`).
- `group owners-missing [--all-types]`: one paged advanced query, `$filter=owners/$count eq 0`, never a request per
  group. Graph keeps no owners for distribution, mail-enabled security and synced groups, so they are left out and
  counted under the table unless `--all-types` asks for them.
- `team list` (`Groups/WorkspaceLookups.cs`): `GET /teams`, which fills the id, the name, the description and the
  visibility only. `site list [--search <words>]`: `GET /sites?search=` (`*` for every site the account can see).
- `trace --sender <address> | --recipient <address> [--days N] [--until <time>]` (`Exchange/MessageTraceLookup.cs`):
  `GET /admin/exchange/tracing/messageTraces` with a `receivedDateTime` window and the addresses as `$filter`, pages
  of 5000. The service answers at most 10 days per request, back to 90 days, ending no later than now: a window
  outside that is a usage error with a hint (`--until` moves the window back) and is never sent, so an empty answer
  always means no message. The service allows 100 requests per 5 minutes per tenant; a 429 goes through the
  client's throttling budget.

## The Exchange Online sidecar

Exchange Online is reached in three layers behind one interface (D-004): Graph where the feature exists (message
trace, the usage report, mailbox settings), the Exchange Online Admin API where its six preview endpoints
reach, and otherwise a long-lived `pwsh` child process running `aegis-exo-sidecar.ps1` with the
`ExchangeOnlineManagement` module. The Core and the script talk over stdin and stdout, one JSON object per line
tagged `{"aegis":1,…}`; everything else the process prints is sent to stderr (the script swaps the console's
standard output for its error stream at start-up and keeps the original for the protocol), so a module banner
cannot corrupt the channel. Only the cmdlets on the script's allow-list run. The window keeps the sidecar warm;
the command line starts it on demand. `M365AEGIS_EXO_MODULE_ROOT` makes the script look for the module in one
folder only, which the tests use for the fake module. The doctor checks pwsh 7.6, the module version (from the same
folder when the variable is set) and the script's hash: the script is also embedded in the Core
(`Exchange/SidecarScript.cs`), and the copy next to the exe has to have the same SHA-256, since it runs with the
administrator's Exchange Online token (see [The doctor](#the-doctor)).

## The front end

`gui/web` is a Vite app: React 19 with TypeScript, Tailwind 4 through the Vite plugin, shadcn/ui (the `base-nova`
style, components generated into `src/components/ui` and committed as the CLI writes them), Biome for lint and
format, Vitest for the tests. The bridge to the host is `src/bridge/`: the protocol (the same shape as
`M365Aegis.Core.Bridge`, checked against the shared samples under `tests/contracts/bridge`) and the transport,
which uses the host's `window.external` when the host injected it and a mock in a plain browser, so `npm run dev`
shows the page without the exe. The GUI project's `BuildFrontend` target runs `npm ci` and `npm run build` when
the sources are newer than `dist/index.html` and copies `dist/` next to the exe as `wwwroot/`;
`-p:SkipFrontendBuild=true` skips both. During development, `m365-aegis-gui --dev-url http://localhost:5173`
loads the dev server with hot reload, with the developer tools and the browser's context menu on.

The host (D-009, measured against Photino.NET in task 014) is a WinForms form with the `WebView2` control of
Microsoft's WebView2 SDK, in `src/M365Aegis.Gui/Host/WinFormsWebView2Host.cs` behind `IUiHost`:

- The shipped `wwwroot/` is served from the virtual host `https://m365-aegis.example/`
  (`SetVirtualHostNameToFolderMapping`, no cross-origin access; `.example` never resolves to a real site), so the
  page has an https origin and its ES modules load without a browser flag; a `file://` page would refuse them.
- The bridge: a script added on document creation defines `window.external.sendMessage` and `receiveMessage` over
  `chrome.webview`; the host takes a message (`WebMessageReceived`) only from the page's own origin, answers with
  `PostWebMessageAsString`, cancels a navigation away from that origin and refuses new windows.
- The browser's data folder is `webview2\` under the home folder (`CoreWebView2Environment.CreateAsync` with it as
  the user data folder), never next to the exe.
- The form's handle (`IUiHost.WindowHandle`) exists before the page starts and is the top-level window the
  broker's account picker takes as its parent; `--smoke` prints the time to `ready` and whether the handle is a
  top-level window.
- WinForms needs the `net10.0-windows` target, so the GUI project is the only one on it; the Core and the Cli stay
  plain `net10.0`. `EnableWindowsTargeting` lets the Linux `dotnet` job restore the Windows Desktop targeting pack
  and build the project. The window therefore needs the .NET Desktop Runtime 10, the command line only the .NET
  Runtime 10. The WebView2 package references its WPF control on every .NET project; the GUI project drops that
  reference before the build (it would pull in WPF's `WindowsBase` and fail on a version conflict), so nothing WPF
  ships. The WebView2 runtime itself is the Evergreen one Windows 11 carries.
- The WebView2 browser processes outlive a closed window by a few seconds (about six, measured 2026-10-29), so a
  `Start-Process -Wait` on the exe, as the `windows` job's smoke step runs it, waits for them too.

The front end's packages, with their reasons (`package.json` carries no comments): `react` and `react-dom`
(the UI); `vite` and `@vitejs/plugin-react` (the build and the dev server); `typescript` (strict types; the 7.x
native compiler, which dropped `baseUrl`, so the `@/*` mapping in `tsconfig.json` is relative to the config and
Vite's `resolve.alias` resolves it at build time); `tailwindcss` and `@tailwindcss/vite` (the utility CSS the shadcn
components are written in); `shadcn`, `@base-ui/react`, `class-variance-authority`, `cn`, `lucide-react`,
`tw-animate-css` and `@fontsource-variable/geist` (what `shadcn init` and its components import: the component
primitives, variant classes, the class-name helper, the icons, the animations, the font); `vitest` (the tests);
`@biomejs/biome` (lint and format in one pinned binary); `@types/*` (the type definitions).

## The output contract

Every answer of the command line is one of two renderings of the same data (D-006): a table for a terminal, or
the JSON envelope `{"ok":…,"data":…,"error":{"code","message","hint"},"meta":{"requestIds","elapsedMs",
"backend"}}` on one line for a pipe or an agent; `--json` and `--table` force one. The exit codes are the
`ExitCode` enum in the Core and never change meaning:

| code | error code | meaning |
| --- | --- | --- |
| 0 | | ok |
| 2 | `usage` | an unknown command or option, a missing argument; nothing was called |
| 3 | `auth` | no usable sign-in |
| 4 | `not-found` | the named object (or profile) does not exist |
| 5 | `throttled` | the service throttled the request and the retries ran out |
| 6 | `partial` | some items of a bulk operation failed; the data lists both |
| 7 | `refused-without-yes` | a write without `--yes`; the plan is the data |
| 8 | `forbidden` | the service refused the account or the app: a permission not consented, a role missing (task 003) |
| 9 | `service-error` | the service answered with an error no other code covers (a bad request, a server error after the retries), or could not be reached (task 003) |
| 10 | `doctor-failed` | a required doctor check failed; the report is the data |
| 70 | `internal` | the tool broke: an unexpected exception stopped the command (task 021); `EX_SOFTWARE` of sysexits.h |

A write is a plan the command prints
under `--dry-run` and applies under `--yes`; the window shows the same plan and asks for a confirmation.

In code the path is one. A command's work returns a `CommandResult<T>` (`M365Aegis.Core.Output`): the data,
whole on success or partial on a failure, and an `AegisFailure` when something went wrong, which is a
`FailureKind` (its stable kebab-case error code and its exit code follow from the kind), one sentence and a
hint. An operation of the core that cannot finish throws an `AegisException` carrying such a failure.
`CommandRunner` in the Cli times the work, turns the exception into the failed result, turns any other
exception (a bug) into the `internal` failure, whose message is the exception's type and its message, scrubbed
like a log line, and whose hint names the log file that got the stack trace (the stack trace never reaches the
answer; a cancellation is left to the command line library), builds the meta
(`Backend` names the backend) and hands the result to `Renderer`, which writes the envelope on stdout, or the
table: the data on stdout through the result type's `ITableView<T>` (the columns, a row per item, the footer
lines), and the failure as an `error: <message> (<code>)` line and a `hint:` line on stderr. The exit code is
the result's. A usage error takes the same path, so a shell or an agent never meets a third shape. A new
command therefore adds a result type to the Core (plain data without presentation; the window carries the same
type over the bridge), an `ITableView<T>` next to the command, and `CommandRunner.Run(mode, backend, work,
view)` as the command's action; it never writes to the console itself. A new failure condition gets a new
`FailureKind` with a new code and a new `ExitCode` number, never a reused one. `doctor` is the first command on
the path: its result type is `DoctorReport`, its table view sits in `DoctorCommand`. With
`M365AEGIS_TEST_FAULT=internal-error` in the environment the runner throws before any work, which is how the
child-process tests see an internal error the way a shell does.

The snapshot tests (Verify) hold both renderings byte for byte over the scrubbed doctor reports under
`tests/fixtures/doctor/`, which `tests/shared/DoctorSamples.cs` reads back with the contract's own JSON options:
the envelope in `tests/M365Aegis.Core.Tests/Snapshots/*.verified.json`, the table and the renderer in
`tests/M365Aegis.Cli.Tests/Snapshots/*.verified.txt`. A snapshot test that fails writes the rendering it got as
a `*.received.*` file next to the expected one (gitignored) and prints both. When the new rendering is the
intended one, the change of the contract is accepted by renaming the received file to `.verified.` and
committing it; `scripts/check-fixtures.py` scans the snapshot folders with the fixtures, so an accepted
snapshot cannot carry tenant data either. `DiffEngine_Disabled=true` in the environment keeps Verify from
opening a diff tool in a non-interactive run.

### The doctor

`m365-aegis doctor` is what a colleague runs first when the tool does not work, and the prerequisite check the
framework-dependent publish relies on (D-007; task 008). Each check is a row with `pass`, `warn`, `fail` (`FAIL`
when it is required) or `skip`; a warning or a failure carries a hint, which the table prints under the rows as a
`fix <check>: …` line and the envelope as the check's `hint`. A required check that fails makes the report fail
with exit code 10; every other outcome exits 0. `--offline` skips the sign-in and the endpoints, the two checks
that need the network.

| check | required | pass | warn | fail (with the fix) |
| --- | --- | --- | --- | --- |
| `home` | yes | the folder exists | not created yet | a file, not a folder |
| `dotnet-runtime` | no | the runtime the process runs on (a fact, so it has no warn or fail) | | |
| `pwsh` | yes | 7.6 or later on PATH | the version is unreadable | missing (`winget install Microsoft.PowerShell`) or older |
| `exchange-online-module` | no | 3.10 or later | older (`Update-Module`) | not installed (`Install-Module … -Scope CurrentUser`); skipped without pwsh |
| `sidecar-script` | yes | the SHA-256 of `sidecar\aegis-exo-sidecar.ps1` matches the copy compiled into the Core | differs in its line endings only | missing or changed (reinstall the release zip) |
| `webview2-runtime` | no | the `pv` of the EdgeUpdate client key, 120 or later | older (Edge Update is blocked) | not installed (`winget install Microsoft.EdgeWebView2Runtime`); skipped off Windows |
| `config` | no | the profiles and the default | no file (the login command), no profile, or no default (`profile use`) | the file does not parse, or a profile lacks a value |
| `device` | no | Entra-joined with a primary refresh token | registered only, or joined without a primary refresh token (task 013: the silent sign-in as the Windows account fails, the profile's account signs in instead), or `dsregcmd` unreadable | neither joined nor registered (Settings > Accounts > Access work or school) |
| `sign-in` | no | the silent sign-in of `whoami`: the account and each token's expiry | no profile, or a resource without a token (the consent) | the silent sign-in failed (`login`) |
| `endpoint-login`, `endpoint-graph`, `endpoint-exchange` | yes | any HTTP answer from `login.microsoftonline.com`, `graph.microsoft.com`, `outlook.office365.com` | a proxy asks for authentication (407), or slower than 3 s | no answer within 10 s (the network, the proxy, the firewall) |

The pwsh and module checks are one pwsh call; the device check reads `AzureAdJoined`, `WorkplaceJoined` and
`AzureAdPrt` of `dsregcmd /status` and nothing that names the tenant. Everything a check reads comes through
`DoctorEnvironment` (`Diagnostics/DoctorEnvironment.cs`), which the tests replace part by part: the pwsh call, the
registry, `dsregcmd` (the made-up outputs under `tests/fixtures/doctor/dsregcmd-*.txt`), the two copies of the
script, the sign-in and the endpoint probe.

## CI and releases

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, under a minutes budget (D-005).
Its first job, `changes`, diffs the change (a pull request against its merge base, a push against the commit
before it) and sets a `code` output: when every changed path is under `docs/`, `tasks/` or `.claude/memory/`, or
is `CHANGELOG.md` or a root markdown file, the five build jobs below are skipped and the run is green in under a
minute; `lint`, `workflow-lint` and `commit-rules` run on every change, and a tag, a manual run or an unknown
base is always the full run (task 023). The skip is a job-level `if:`, never a path filter on the trigger: a
required check whose workflow never starts stays pending and blocks the merge, while a job skipped by its
condition passes its check. The jobs: `dotnet` (locked restore, `dotnet format --verify-no-changes`, the build with the analyzers, the
Core and Cli tests), `deps` (the vulnerability script, `nuget-license` against `licenses/allowed.json` over the
shipped projects, with `licenses/overrides.json`, which the release script's notices read too, naming the
license of a package that ships it as a file instead of an SPDX expression: the broker's native runtime, `Microsoft.Identity.Client.NativeInterop`, comes
under the Microsoft Software License Terms and is allowed as `LicenseRef-Microsoft-Software-License-Terms`, and
the WebView2 SDK, `Microsoft.Web.WebView2`, ships a BSD-3-Clause text and is named `BSD-3-Clause`; an override
names one version, so a bump of either package updates its line;
`npm audit` and the front end's license script), `frontend` (Biome, the type check, Vitest,
the Vite build), `sidecar` (PSScriptAnalyzer and the Pester tests against the fake module, with the runner's own
pwsh), all four skipped on a docs-only change, `lint` (the changelog check, skipped on a tag, see [The changelog](#the-changelog); the fixture check; the
commit-rules self-test) and `workflow-lint` (zizmor over the workflows and `dependabot.yml`; a finding accepted on
purpose carries `# zizmor: ignore[<audit>]` on the step's name line with its reason in the comment above). The
one Windows job, `windows`, runs on pull requests and tags only, and not on a docs-only change: the full build
with the front end, the tests,
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
