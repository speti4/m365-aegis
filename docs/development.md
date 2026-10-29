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
device-code sentence is printed on stderr, so stdout stays the envelope. The window signs in through the same
`AuthService.LoginAsync` from its sign-in card (task 040): its own form is the picker's parent instead of the
console's root owner, the picker's call is started on the window's UI thread, as MSAL asks of a UI application
(`MsalTokenBroker.Create` with the parent and the host's `InvokeOnUiThreadAsync`; checked against the MSAL.NET
"Acquiring tokens interactively" and WAM pages and `MsalError.WamUiThread` on 2026-10-29), and the device code goes
to the card as a bridge notification instead of stderr (see [The front end](#the-front-end)). `Auth/MsalTokenBroker.cs` in the Core
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
line here:

- `call` (task 003): nothing of its own; the endpoint it calls needs what its reference page names.
- Groups (task 005): `GroupMember.Read.All` for the groups, their members and owners and `group owners-missing`,
  and `User.ReadBasic.All` for the names of members and owners that are users (without it Graph returns their ids
  and types only).
- Teams (task 005): `Team.ReadBasic.All` for `team list`.
- SharePoint (task 005): `Sites.Read.All` for `site list`; with a delegated token the site search is trimmed to the
  sites the signed-in account can open (`getAllSites`, every site regardless, is application-only).
- Audit (task 006): `AuditLog.Read.All` for `signin list` and `audit list`, the sign-in log and the directory
  audit log; the account also needs a role that reads them (Reports Reader, Security Reader or Global Reader), and
  the sign-in log a Microsoft Entra ID P1 or P2 license in the tenant (without one `signin list` exits 8 with a
  sentence that says so). `Application.Read.All` for `secrets expiring`, the secrets and certificates of the
  application registrations and the service principals.
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
and, only under the recursive `--verbose` option (and `m365-aegis-gui --verbose` for the window, task 027), MSAL's
own log of the sign-in (`WithLogging` with PII logging off, every level, so a broker error code or a Conditional
Access verdict names its step). A run without
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

`call <METHOD> <path> [--beta] [--body <json|@file>] [--all] [--advanced] [--yes]` is the raw request: the path under the
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
  these lists is an advanced query (`ConsistencyLevel: eventual`, `$count=true`). `group members --transitive`
  (task 033) reads `/groups/{id}/transitiveMembers`, the flat list of every member of the nested groups too; it
  does not say through which group a member came, so the direct list is asked for beside it and the row's
  membership column says `direct` or `nested` (the envelope's `nested` field), next to the member's type (`user`,
  `group`, `device`, `servicePrincipal`, `orgContact`).
- `group owners-missing [--all-types]`: advanced queries on `owners/$count eq 0`, never a request per group.
  Graph keeps no owners for distribution, mail-enabled security and synced groups, so by default only the two
  kinds it keeps them for are asked for, side by side (task 032): the Microsoft 365 groups
  (`groupTypes/any(c:c eq 'Unified')`) and the security groups without mail (`mailEnabled eq false and
  securityEnabled eq true and NOT(groupTypes/any(…))`), each `$top=999` with the six group fields, and next to them
  `GET /groups/$count?$filter=owners/$count eq 0`, whose number less the listed groups is the left-out count under
  the table; the synced groups are picked out client-side. A raw `$count` answers `text/plain` only and refuses a
  request that accepts only JSON (415), so the client sends `Accept: text/plain` for it (`GraphRequest.Accept`;
  `call` does the same for a path ending in `/$count`). The one wide query, which the left-out kinds filled
  with pages, answers `--all-types`, and answers instead should Graph refuse a narrowed filter or the count (400,
  406 or 415). Measured on a tenant of 2445 groups, 2117 of them without an owner in Graph (2026-10-29): the wide
  query is three pages in about 1.35 s, the narrowed Microsoft 365 query one page in about 0.65 s, which is the
  floor of the default; the command answered in 1.25 s warm and 2.1 s cold (four requests: the security query
  took two pages), against 7.9 s for the one wide query of task 005, the rest being the process start and the
  token. `call GET "<path>" --advanced` sends the same `ConsistencyLevel: eventual` for a raw
  advanced query.
- `team list` (`Groups/WorkspaceLookups.cs`): `GET /teams`, which fills the id, the name, the description and the
  visibility only. `site list [--search <words>]`: `GET /sites?search=` (`*` for every site the account can see).
- `trace --sender <address> | --recipient <address> [--days N] [--until <time>]` (`Exchange/MessageTraceLookup.cs`):
  `GET /admin/exchange/tracing/messageTraces` with a `receivedDateTime` window and the addresses as `$filter`, pages
  of 5000. The service answers at most 10 days per request, back to 90 days, ending no later than now: a window
  outside that is a usage error with a hint (`--until` moves the window back) and is never sent, so an empty answer
  always means no message. The service allows 100 requests per 5 minutes per tenant; a 429 goes through the
  client's throttling budget. `--status <status>` (delivered, failed, pending, expanded, quarantined,
  filteredAsSpam, gettingStatus, none; any case, `filtered-as-spam` too) and `--subject <text>` (task 035) are
  server-side: `status eq '…'` and `contains(subject,'…')` join the `$filter`, both documented filters of the trace
  (https://learn.microsoft.com/exchange/monitoring/trace-an-email-message/graph-api-message-trace). Should the
  service refuse them (400), the window is traced again without them and they are applied client-side (the status
  exact, the subject a case-insensitive contains), so a refused filter is never the answer; the envelope's
  `filteredBy` (`service` or `client`) and the line under the table say which way it went.
- `trace details <trace id | Message-ID> [--recipient <address>]` (`Exchange/MessageTraceDetailsLookup.cs`, task
  034): the delivery steps of one message per recipient (time, event, action, description, and the event's XML data
  in the envelope) from the function `messageTraces/{id}/getDetailsByRecipient(recipientAddress='…')`, which takes
  a trace id and one recipient. A trace id with `--recipient` is one request; otherwise the message's trace rows come
  first (`$filter=id eq …` or `messageId eq …`, over the service's default window of 48 hours) and name the
  recipients, at most 10, whose steps are asked for side by side. An older message is reached with the trace id
  from `trace --until …` and `--recipient`.
- `signin list` and `audit list` (`Audit/`, task 006) read a window of the log, newest first: `--days N` or
  `--since <date>` for the start, `--until <date>` for the end (`createdDateTime le` / `activityDateTime le`; `--days`
  counts back from it), up to `--limit` entries (1000 by default). A list that stops at the limit sets `truncated`
  and names the time of the oldest entry it listed (`oldest` in the envelope); the line under the table gives the
  window that continues it, `--since <start> --until <oldest>` (task 038). `audit list --user <upn>` keeps the
  activities that user started, `initiatedBy/user/userPrincipalName eq …` in the `$filter` beside the window,
  `--activity` and `$orderby` (task 037), and the line under the table names the user.
- `signin list --kind interactive|non-interactive|service-principal|managed-identity` (task 039) is the one lookup
  on the beta endpoint: v1.0 lists the interactive user sign-ins only, and the other kinds are reached through
  beta's `signInEventTypes/any(t: t eq 'nonInteractiveUser')` (`interactiveUser`, `servicePrincipal`,
  `managedIdentity`) in the `$filter` (https://learn.microsoft.com/graph/api/signin-list?view=graph-rest-beta). The
  report's `endpoint` says which version answered (`v1.0` without `--kind`), and the line under the table names the
  beta dependency, whose answers may change shape; the service principal and managed identity rows show the
  service principal and the resource in place of the user and the app, and `--user` with them is a usage error.
  Same permission, roles and Microsoft Entra ID P1 or P2 license as the v1.0 log.

## The Exchange Online sidecar

Exchange Online is reached in three layers behind one interface (D-004): Graph where the feature exists (message
trace, the usage report, mailbox settings), the Exchange Online Admin API where its six preview endpoints
reach, and otherwise a long-lived `pwsh` child process running `aegis-exo-sidecar.ps1` with the
`ExchangeOnlineManagement` module (tasks 015 and 016). The doctor checks pwsh 7.6, the module version and the
script's hash: the script is also embedded in the Core (`Exchange/SidecarScript.cs`), and the copy next to the exe
has to have the same SHA-256, since it runs with the administrator's Exchange Online token (see
[The doctor](#the-doctor)); the session refuses to start a copy the doctor would fail.

**The process and the protocol.** `Exchange/Sidecar/SidecarClient.cs` starts `pwsh -NoProfile -NonInteractive
-NoLogo -File sidecar\aegis-exo-sidecar.ps1` (pwsh from `PATH`) and speaks to it over stdin and stdout, UTF-8, one
JSON object per line tagged `{"aegis":1,"id":…,"type":…}`, one request in flight at a time, the reply matched by its
id. Everything else the process prints goes to stderr (the script swaps the console's standard output for its
error stream at start-up and writes the protocol to the raw stream), so a module banner cannot corrupt the
channel; the client keeps the last 20 stderr lines, scrubbed like a log line, for the message of a failure. The
messages: `hello` (the pwsh and module versions and the script's allow-list), `connect` and `reconnect` (the token,
`organization` and/or `userPrincipalName`; the reply carries the import and connect times), `invoke` (a cmdlet, its
parameters, the properties to keep of each object, and `input`, the values piped into the cmdlet, which is how the
`Get-EXO*` cmdlets read every mailbox in one call), and `exit` (the script disconnects and ends; the client kills a
process that has not ended ten seconds later). Every value an object carries back is plain: a string, a number, a
boolean, null or a list of strings, which `ExoObject` reads into the typed records of the lookups. A run that
returned objects keeps the non-terminating errors of the items that failed next to them (`errors`); a run that
returned none is an error reply. An error reply carries `code`, `message`, and PowerShell's error id and category;
the script replaces every token it was handed, and anything shaped like a JWT, in all of them.

**The allow-list.** `Exchange/Sidecar/ExoCmdlets.cs` is the one place a cmdlet is admitted: `Get-EXOMailbox`,
`Get-EXOMailboxPermission`, `Get-EXORecipientPermission`, `Get-EXOMailboxStatistics`, `Get-EXORecipient` and
`Get-AcceptedDomain`, exactly the cmdlets the lookups use. The session refuses any other name before anything is
sent (exit code 12); the script carries the same list as its own guard (`$script:AllowedCmdlets`), a Core test
holds the two equal, and the session also compares the list the script reports in `hello`. A cmdlet joins the list
with the lookup that needs it.

**The connect and the token.** `Exchange/Sidecar/ExchangeSession.cs` holds one sidecar: the command line one per
run (started on the first cmdlet, ended with the command), the window one for its lifetime (it is safe to call
from several tasks, and a sidecar that died is replaced on the next cmdlet). The sign-in comes first, so no pwsh
starts without a profile and a token: the token for `https://outlook.office365.com/.default` from
`AuthService.GetTokenAsync` (silent, task 009), passed as `Connect-ExchangeOnline -AccessToken <token>
-Organization <tenant>.onmicrosoft.com -SkipLoadingFormatData -CommandName <the REST cmdlets of the allow-list>
-ShowBanner:$false` (the `Get-EXO*` cmdlets are the module's own and need no import; the format data is never used,
since the protocol never formats an object). It is the fastest of the three variants task 011 measured on
2026-10-29 with the token hand-off, five fresh processes each: the connect's median 0.48 s against 1.05 s with
`-SkipLoadingFormatData` alone and 1.98 s with neither, the first answer after 2.4 s against 3.2 s and 4.2 s
(`docs/origin.md`, "Problem"). The organization is the profile's tenant when it is a
`.onmicrosoft.com` name, else the initial domain of `GET /organization` (Graph, `User.Read`).
`M365AEGIS_EXO_CONNECT=upn` passes `-UserPrincipalName <the profile's account>` instead of the organization, and
`both` passes both. The token is never logged or written: the records that carry it leave it out of their
`ToString`. The session tracks the token's `ExpiresOn` and, five minutes before it, asks for a fresh token and sends
`reconnect` (the script disconnects and connects again).

**The exit codes.** The module reports a sign-in, a permission, a missing object and throttling all as a
PowerShell error, so `Exchange/Sidecar/SidecarFailures.cs` reads the message for what the service said: a missing
pwsh or module, a script that does not match the build, a process that died or stopped answering, and a connect the
service refused for a reason it did not name as a sign-in or a permission are 11 (`sidecar-unavailable`, with
`doctor` as the hint); a token the service refused (a 401, an expired token, an `AADSTS` error) is 3; a 403, an
access denial or a missing role is 8; a cmdlet whose object was not found is 4; throttling (a 429, Server Busy, a
micro delay, a budget) is 5; a cmdlet off the allow-list is 12; any other cmdlet failure is 13 (`cmdlet-failed`);
a bulk run with items that failed is 6 with the rest of the answer as the data. An error reply that means the Core
sent a wrong message (`bad-request`, `not-connected`, `unknown-type`) is a bug, exit code 70. The message of a
failure is the service's one sentence (the `message` of a REST error's JSON body, without the server name, at most
300 characters; a mailbox that is not found is `mailbox '<as given>' not found`), and the whole text goes to the log
under `--verbose` only.

**The mailbox commands** (`Exchange/MailboxLookups.cs`, `Commands/MailboxCommand.cs`; the Cli's `ExchangeRunner` is
`GraphRunner`'s twin, with `pwsh` as the backend). Each pays about 2.8 s before its first cmdlet (pwsh 0.5 s, the
module's import 1.1 s, the connect 1.3 s; tasks 009 and 016, measured 2026-10-29), which the window's warm sidecar
saves:

- `mailbox list [--shared]`: `Get-EXOMailbox -ResultSize Unlimited` (`-RecipientTypeDetails SharedMailbox`), by
  display name.
- `mailbox access <mailbox>`: FullAccess (`Get-EXOMailboxPermission`), Send As (`Get-EXORecipientPermission
  -AccessRights SendAs`) and Send on Behalf (the mailbox's `GrantSendOnBehalfTo`, resolved to addresses with one
  piped `Get-EXORecipient`); the direct, allowed grants only, without `NT AUTHORITY\…` entries. A grant to a group
  is listed as the group; its members are not expanded.
- `mailbox access --user <upn>`: the reverse lookup in four bulk calls whatever the tenant's size: the user as a
  recipient (the names Exchange may use for it in a permission entry), every mailbox with its Send on Behalf list,
  the FullAccess entries of every mailbox (the mailboxes' object ids piped into `Get-EXOMailboxPermission`), and
  the organization's Send As entries. A mailbox whose permissions could not be read makes the answer partial (exit
  code 6). The throttling budget is the module's: the `Get-EXO*` cmdlets back off and retry a throttled request
  themselves, and a request that still fails is exit code 5. The module sends one request per piped mailbox, about
  0.2 s each: over 620 mailboxes the lookup took 138 s, unthrottled (task 016, measured 2026-10-29).
- `mailbox forwarders`: every mailbox with `ForwardingSmtpAddress` or `ForwardingAddress` set, whether it keeps a
  copy (`DeliverToMailboxAndForward`), and whether the target is outside the accepted domains (`Get-AcceptedDomain`;
  a `ForwardingAddress` is a recipient, resolved to its address with one piped `Get-EXORecipient`), the external
  ones first.
- `mailbox size [<mailbox>] [--top N]`: `Get-EXOMailboxStatistics` for one mailbox or every mailbox (piped), the
  largest first; the size is read from the service's text (`1.2 GB (1,288,490,188 bytes)`). Every mailbox costs a
  request, about 0.12 s each (80 s over 620 mailboxes).

The account needs an Exchange Online role that reads recipients and their permissions (View-Only Organization
Management or Recipient Management; the Exchange Administrator and Global Administrator roles include them); the
app registration needs `Exchange.Manage`.

**Tests and measurements.** `M365AEGIS_EXO_MODULE_ROOT` makes the script look for the module in one folder only,
which the tests point at the fake module under `tests/sidecar/modules` (its made-up mailboxes are listed in
`tests/sidecar/README.md`); `M365AEGIS_EXO_TEST_ORGANIZATION` makes the command line connect to that organization
with a placeholder token and no sign-in, which only the fake module accepts. The Pester tests drive the script on
its own, the Core tests drive it through the client and the session, the Cli tests through the exe, all with a real
pwsh child on Linux and Windows. Three hidden commands are the live probes of the Exchange bundle:
`m365-aegis probe exchange-connect [--connect-with organization|upn|both]` (task 009: a connect, a cmdlet, a
reconnect with a fresh token), `probe exchange-measure [--runs 5] [--variants default,skip-format-data,command-name]`
(task 011: the broker's token asked for once, silently, then per variant fresh sidecars connected with it over the
protocol exactly as a command's sidecar is, timing the start, the module's discovery and import, the connect and a
first REST cmdlet, with the median and the spread per variant, the versions and the OneDrive checks of the module
path and the Documents folder; `connect` takes `skipLoadingFormatData` and `commandName` set to false for the other
variants) and `probe admin-api` (task 010). They print timings, permissions, statuses and the shape of an answer,
never a token, a path or a value, and none of them signs in interactively: the module's own sign-in is never
used.

## The front end

`gui/web` is a Vite app: React 19 with TypeScript, Tailwind 4 through the Vite plugin, shadcn/ui (the `base-nova`
style, components generated into `src/components/ui` and committed as the CLI writes them), Biome for lint and
format, Vitest for the tests. The bridge to the host is `src/bridge/`: the protocol (the same shape as
`M365Aegis.Core.Bridge`, checked against the shared samples under `tests/contracts/bridge`), the transport, which
talks to WebView2 directly (task 030: `chrome.webview.postMessage` with the message's JSON text, and
`chrome.webview`'s `message` event for the host's text replies; the host injects nothing) and a mock in a plain
browser (`src/bridge/mock-transport.ts`), so `npm run dev` shows the page with made-up data and without the exe
(`?mock=signed-out` in the address starts it signed out, to try the sign-in card), and the client
(`src/bridge/client.ts`), which gives each request an id and resolves it with the reply that repeats it, turns a
reply it cannot read or a host silent for two minutes (sixteen for a `login`, which waits for the user) into an
`error` reply, and hands a message without an id, a notification of the host, to its `onNotification` handlers.

The messages (tasks 017, 040 and 045; `BridgeMessageType` in C#, `MessageType` in TypeScript, one sample each under
`tests/contracts/bridge`): the page sends the `ready` notification, then

- `ping`, answered with `pong` (the product and the version);
- `whoami`, answered with `signed-in` (the core's `SignInInfo`) or `signed-out` (the profile it was tried for, why
  and what to do);
- `login`, with `{"deviceCode": true}` or no payload (task 040): `AuthService.LoginAsync` for the window's profile,
  silently when it can, else the broker's account picker over the window, else (or when asked) a device code,
  answered with `signed-in`. While a device-code sign-in waits for the browser step, the host sends the
  `device-code` notification (no id: the page to open, the code, the service's sentence, the expiry), which the
  sign-in card shows with a copy button; the page opens no other site, so the address is shown to type. A profile
  is never written from the window: without one, `login` is the `auth` error with the command line's
  `login --tenant … --client-id … --account …` as the hint, and the card offers no "Sign in" until it exists;
- `logout`, answered with `logged-out` (the core's `SignOutInfo`); it also ends the window's Exchange Online
  session, whose sidecar is connected with the account's token;
- `group-members` with `{"group": "<name, address, id or word>"}`, answered with `members` (the core's
  `GroupMembers`);
- `mailbox-list` with `{"shared": true}` or no payload, answered with `mailboxes` (the core's `MailboxList`), and
  `mailbox-access` with `{"mailbox": "<address, name or id>"}` or `{"user": "<upn>"}` (one of the two), answered
  with `mailbox-holders` (`MailboxAccess`) or `user-grants` (`UserAccess`) (task 045). A partial answer of the
  reverse lookup (exit code 6 on the command line) is the `user-grants` reply, whose `failed` list the page shows as
  a warning, as the envelope carries the data with the error.

Any request can be answered with `error`: the envelope's `code`, `message` and `hint` (a failure of the core,
`unknown-type`, or `internal` with the stack trace in the log). A payload is written the way the envelope writes
its data (D-006), so the window reads the shape `--json` shows. The C# tests dispatch each request sample over a
scripted sign-in, the recorded Graph answers and a session over the fake Exchange Online module and hold the reply
to its sample field by field (the `device-code` notification to its sample's shape, its expiry being the moment's
own); the Vitest tests read the same samples. The host's `BridgeDispatcher` (`M365Aegis.Core.Bridge`) runs on
`BridgeServices.ForWindow`: the command line's `AuthService` over the same home folder (MSAL through the Windows
account manager, the window as the picker's parent and its UI thread as the picker's thread, see
[The sign-in and the home folder](#the-sign-in-and-the-home-folder)), `GraphConnection` for the Graph client
(`M365AEGIS_GRAPH_FIXTURES` makes the window read the fixtures too, which is how the page is tried without a
tenant), the window's Exchange Online session and the internal-error log the command line writes; the window takes
the default profile.

**The window's Exchange Online session** (task 045, `Bridge/WindowExchange.cs`): one `ExchangeSession` for the
window's lifetime, over the window's own sign-in and the command line's `ExchangeConnection` parts (pwsh from
`PATH`, the script next to the exe after its hash check, the token silently, the organization), with
`M365AEGIS_EXO_TEST_ORGANIZATION` and `M365AEGIS_EXO_MODULE_ROOT` working as on the command line, which is how the
mailbox page is tried against the fake module without a tenant. Nothing starts before the first mailbox request;
that one pays pwsh, the module's import and the connect (about 2.4 s against the service, task 011), every later
one only its cmdlets (two lookups in a row against the fake module: 541 ms, then 19 ms, one pwsh, 2026-10-29). The
session renews its token on the next cmdlet five minutes before the expiry and replaces a sidecar that died (task
015); a keep-warm timer of the window looks every minute and reconnects with a fresh token seven minutes before the
expiry, so a request after an idle hour does not wait for the reconnect, and a tick that cannot renew (the sign-in
expired) leaves it to the next request, which answers the failure. `logout` ends the session; the window's close
disposes it after the message loop ends, which sends the sidecar `exit` (the script disconnects). Under `--verbose`
each mailbox request writes one `window:` line to the log: the time and what the session did (`sidecar starts 1,
renewals 0, last connect … ms`), never a token.

The page (`src/App.tsx`, `src/components/`): the sign-in card (its state machine in
`components/sign-in-state.ts`: `whoami` reads it, `login` waits with the device code when one comes, `logout`
reads it again; "Sign in" and "Sign in with a code" when signed out with a profile, "Sign out" when signed in), the
group members card with the table, the mailbox access card (a mailbox or a user, the shared mailboxes to pick
from, the holders or the grants in a table, the partial warning, and the time of the answer under the table), and a
toast (sonner) for every `error` reply.

**The task card design sketch** (task 018, `src/sketch/`): the card list (grant FullAccess on a shared mailbox, add
a group owner, check a user's access), one card's steps (pick, the plan, the confirmation, the result) and the
states (signed out, working, refused, partial), light and dark, in the shadcn/ui components, over made-up data and
without a bridge request: `task-card-flow.ts` is the flow's state machine (tested with Vitest), `sketch-data.ts`
the made-up core, whose plan shape is made up and marked so on the page (the real `Plan` comes with M3), and
`task-card-sketch.tsx` the page with its controls (signed in or out, how the made-up core answers, the theme; the
`.light` tokens in `index.css` let it show light under a dark system). It runs in the dev server only:
`npm run dev --prefix gui/web`, then `http://localhost:5173/#/sketch/task-cards`. `main.tsx` reaches it only under
`import.meta.env.DEV`, which the build replaces with false, so neither the route nor the sketch's code is in
`dist/` and the window never shows it.

The GUI project's `BuildFrontend` target runs `npm ci` and `npm run build` when
the sources are newer than `dist/index.html` and copies `dist/` next to the exe as `wwwroot/`;
`-p:SkipFrontendBuild=true` skips both. During development, `m365-aegis-gui --dev-url http://localhost:5173`
loads the dev server with hot reload, with the developer tools and the browser's context menu on.

The host (D-009, measured against Photino.NET in task 014) is a WinForms form with the `WebView2` control of
Microsoft's WebView2 SDK, in `src/M365Aegis.Gui/Host/WinFormsWebView2Host.cs` behind `IUiHost`:

- The shipped `wwwroot/` is served from the virtual host `https://m365-aegis.example/`
  (`SetVirtualHostNameToFolderMapping`, no cross-origin access; `.example` never resolves to a real site), so the
  page has an https origin and its ES modules load without a browser flag; a `file://` page would refuse them.
- The bridge is WebView2's own messaging, with no injected script (task 030): the host takes a message
  (`WebMessageReceived`) only from the page's own origin, hands it to the dispatcher off the UI thread and posts the
  reply with `PostWebMessageAsString` back on it, so several requests can be in flight; it cancels a navigation away
  from that origin and refuses new windows. `IUiHost.Post` sends a notification the page did not ask for (the
  device code), marshalled onto the UI thread, and `IUiHost.InvokeOnUiThreadAsync` starts work there (the account
  picker's call). `--smoke` answers no request, only waits for `ready`, so it never reaches the sign-in, the network
  or a sidecar.
- `--verbose` registers the command line's scrubbing MSAL sink before the first sign-in (task 027): the same
  `logs\` folder, PII logging off, nothing written without the flag.
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
primitives, variant classes, the class-name helper, the icons, the animations, the font); `sonner` and
`next-themes` (the toast of shadcn's `sonner` component, which reads the theme through `next-themes`; without a
theme provider it follows the system's light or dark mode, as the page does); `vitest` (the tests);
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
| 11 | `sidecar-unavailable` | the Exchange Online sidecar could not serve the command: pwsh or the module missing, the script not the build's, the process gone, or a connect the service refused without naming the sign-in or a permission (task 015) |
| 12 | `cmdlet-not-allowed` | a cmdlet off the sidecar's allow-list was asked for; nothing ran (task 015) |
| 13 | `cmdlet-failed` | an Exchange Online cmdlet failed with an error no other code covers (task 015) |
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
| `dotnet-desktop-runtime` | no | the .NET Desktop Runtime the window needs, in the major version of the .NET Runtime (the `Microsoft.WindowsDesktop.App` folders of the same installation) | a patch behind the .NET Runtime (`winget upgrade Microsoft.DotNet.DesktopRuntime.10`) | not installed: the window cannot start (`winget install Microsoft.DotNet.DesktopRuntime.10`); skipped off Windows |
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
registry, `dsregcmd` (the made-up outputs under `tests/fixtures/doctor/dsregcmd-*.txt`), the installed Desktop
Runtime versions, the two copies of the script, the sign-in and the endpoint probe.

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
shipped projects, with `licenses/license-texts.json`, which the release script's notices read too, naming the
license of a package that ships it as a file instead of an SPDX expression: the broker's native runtime, `Microsoft.Identity.Client.NativeInterop`, comes
under the Microsoft Software License Terms and is allowed as `LicenseRef-Microsoft-Software-License-Terms`, and
the WebView2 SDK, `Microsoft.Web.WebView2`, ships a BSD-3-Clause text and is named `BSD-3-Clause`. The file maps
each package's license text, kept verbatim under `licenses/texts/`, to its license (nuget-license's
`--licensefile-to-license-mappings`), not a package version: a version bump that ships the same text, as every
Dependabot bump of the two does, passes without an edit (task 029, tried over WebView2 1.0.4314.35 and NativeInterop
0.20.7 on 2026-10-29; the tool's `--override-package-information` takes one exact version per entry and would have
failed both). A bump whose license text changed fails the job: read the new text, and when the license is still
the one named, replace the file under `licenses/texts/` with it;
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
`sidecar\aegis-exo-sidecar.ps1`, the notices and the guide. No XML documentation file ships, neither ours nor the
packages' (`PublishDocumentationFile` and `PublishReferencesDocumentationFiles` off, task 031; the script fails
when one is left in the folder): they were 919 KB, the WebView2 SDK's 639 KB of it. The publish of 2026-10-29
with the window's first page is 9.8 MB unpacked and 4 MB zipped. Next to the zip it writes `SHA256SUMS.txt` (the
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
  made-up data only: the allowed domains, the `00000000-…` ids, nothing that looks like a token; and no file path
  there is over 190 characters from the repo root, which a worktree would push past git for Windows' 260.
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
line in that task's Verification; nothing it printed is committed before it is scrubbed. A Graph fixture's file
name comes from the hidden `m365-aegis call GET "<path>" --fixture-name`, which computes it as the fixture
transport does, sends nothing and needs no sign-in (task 036).
