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
  module under `sidecar/modules/ExchangeOnlineManagement`), `release/` (Pester: the release's signature, task 085),
  `contracts/bridge/` (the message samples the C# and
  TypeScript tests share), `fixtures/` (recorded, scrubbed answers; `fixtures/README.md`; the Graph
  answers are read from the source tree through `shared/GraphFixtures.cs`).
- `scripts/` — the helper scripts: the release and its signature, the changelog, the commit rules, the fixture check, the
  vulnerability check, the front end's license check, the Pester version pin.
- `docs/` — this page, the usage guide for the support team (`usage.md`, shipped in the zip as `GUIDE.md`), the git
  workflow, the maintainer sessions, origin and research.
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
- Group writes (task 052): `GroupMember.ReadWrite.All` for `group members add|remove`, `Group.ReadWrite.All` for
  `group owners add|remove`, and `User.ReadBasic.All` for the users to add; the account also needs a role that
  changes the group (Groups Administrator, User Administrator, or the group's owner).
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
`M365AEGIS_HOME` overrides the folder; the tests point it at a temporary one. The M5 snapshots are to live in
`snapshots\<profile>\` there, one SQLite file per snapshot, as D-011 proposes (task 083; task 087 builds the store).

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
  `group`, `device`, `servicePrincipal`, `orgContact`). `group members --via` (task 041, implies `--transitive`)
  names on each nested member the direct member group (or groups) it is reached through (the envelope's `via`, a
  list of `{id, displayName}`, and a `via` column): the direct members of every group of the transitive list are
  read with one `members` request per group, twenty to a `$batch` call (one more request whatever the nesting up to
  twenty groups), and followed down from each direct member group, a group met twice followed once. A batch item
  with more than a page reads the rest from its next link. The plain transitive list stays the two lists beside
  the resolution, and its envelope is unchanged (`via` is left out).
- `group owners <group>` on a distribution or mail-enabled security group that is not synced (task 076): Graph keeps
  no owners for it, so the command runs over the Exchange layers (`ExchangeRunner`, as the writes do) and reads the
  group's `ManagedBy` the way the owner writes of task 064 read it (the sidecar's `Get-DistributionGroup`, the names
  resolved with one piped `Get-EXORecipient`, a role group listed by its name; see
  [The writes follow-ups](#the-writes-follow-ups-owners-resume-and-prune)). The envelope's backend is `graph+pwsh` and
  its `fromExchange` is `true`; a Microsoft 365 or security group's owners stay on Graph (backend `graph`, the envelope
  unchanged), and only a synced group keeps the footer that Graph keeps no owners for it.
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
- `group members|owners add|remove <group> <user…>` (task 052) are the groups domain's writes, plans over the lists
  above: see [Writes and plans](#writes-and-plans).
- The mailbox lookups are listed with [the sidecar](#the-exchange-online-sidecar), which runs most of them:
  `mailbox access … --expand-groups` (task 044) reads a group holder's members and a user's groups through the
  group lookups above, batched; `mailbox access --user` splits its bulk read across sidecars and `mailbox size`
  reads Graph's mailbox usage report by default (task 043), `--method` picks another way.
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
reach (task 042, "The Admin API backend" below), and otherwise a long-lived `pwsh` child process running
`aegis-exo-sidecar.ps1` with the `ExchangeOnlineManagement` module (tasks 015 and 016). The doctor checks pwsh 7.6, the module version and the
script's hash: the script is also embedded in the Core (`Exchange/SidecarScript.cs`), and the copy next to the exe
has to have the same SHA-256, since it runs with the administrator's Exchange Online token (see
[The doctor](#the-doctor)); the session refuses to start a copy the doctor would fail.

**The process and the protocol.** `Exchange/Sidecar/SidecarClient.cs` starts `pwsh -NoProfile -NonInteractive
-NoLogo -File sidecar\aegis-exo-sidecar.ps1` (pwsh from `PATH`) and speaks to it over stdin and stdout, UTF-8, one
JSON object per line tagged `{"aegis":1,"id":…,"type":…}`, one request in flight at a time, the reply matched by its
id. A stdout line that is not the reply is skipped, never thrown on (task 081): no JSON object, no protocol version 1,
an `id` that is no string, or the reply to another request (one that timed out before); under `--verbose` the log
names each skipped line with its reason: a JSON object by its `id` and `type`, never its data, any other line by its
first 200 characters. Everything else the process prints goes to stderr (the script swaps the console's standard output for its
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
`Get-EXOMailboxPermission`, `Get-EXORecipientPermission`, `Get-EXOMailboxStatistics`, `Get-EXORecipient`,
`Get-AcceptedDomain` and `Get-EXOMailboxFolderStatistics` (task 042), exactly the cmdlets the lookups use, and the
write cmdlets below. The session refuses any other name before anything is
sent (exit code 12); the script carries the same list as its own guard (`$script:AllowedCmdlets`), a Core test
holds the two equal, and the session also compares the list the script reports in `hello`. A cmdlet joins the list
with the lookup that needs it.

**The write cmdlets** (task 053, a D-004 note of 2026-10-30). The allow-list also admits `Add-MailboxPermission` and
`Remove-MailboxPermission` (FullAccess), `Add-RecipientPermission` and `Remove-RecipientPermission` (Send As) and
`Set-Mailbox` (Send on Behalf and the forwarding), each with the parameters of `ExoCmdlets.WriteParameters` only:
`Set-Mailbox` can change anything on a mailbox, so it runs with `Identity`, `GrantSendOnBehalfTo`,
`ForwardingSmtpAddress`, `ForwardingAddress`, `DeliverToMailboxAndForward` and `Confirm` and nothing else.
`Add-MailboxPermission` also takes `AutoMapping` (task 059: `mailbox access grant … --right FullAccess
--no-automapping`, or `false` in the CSV file's `automapping` column, sends `-AutoMapping:$false`, so Outlook does
not add the mailbox for the holder on its own; without it the parameter is not sent and the service's default, true,
applies), `Remove-MailboxPermission` does not. The flag beside a right other than FullAccess, or on a revoke, is a
usage error; the plan's change names the choice (`FullAccess for … , without automapping`), and a holder who has
FullAccess already is unchanged with its automapping left as it is. The session
refuses another parameter before anything is sent (exit code 12), the script holds the same lists
(`$script:WriteParameters`, a Core test holds them equal) and refuses it too. Every write passes `-Confirm:$false`,
since a sidecar cannot answer a prompt. A parameter that is a JSON object reaches the cmdlet as a hashtable, which is
how a multi-valued property's change is sent (`-GrantSendOnBehalfTo @{Add="bob@contoso.com"}`). The write cmdlets are
REST cmdlets, so the connect's `-CommandName` imports them with `Get-AcceptedDomain`. The Admin API stays read-only:
its documented endpoints are reads. The writes run only from a plan's apply (see [Writes and plans](#writes-and-plans)).

**The member writes of a distribution group** (task 061, a D-004 note of 2026-10-30). The allow-list also admits
`Add-DistributionGroupMember` and `Remove-DistributionGroupMember`, each with `Identity`, `Member`,
`BypassSecurityGroupManagerCheck` and `Confirm` only: `group members add|remove` on a distribution or mail-enabled
security group, whose members Exchange manages and Graph refuses to change. The group is its primary address, the
member a recipient's address; `-BypassSecurityGroupManagerCheck` lets an administrator who is not among the group's
owners (`ManagedBy`) change it, which needs the Organization Management role group or the Security Group Creation and
Membership role (https://learn.microsoft.com/powershell/module/exchangepowershell/add-distributiongroupmember).

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
`reconnect` (the script disconnects and connects again). One token serves every layer of a command (task 049):
`Exchange/ExchangeTokenSource.cs` asks the silent sign-in once and hands the same token and expiry to the command's
sidecar, the extra sidecars of a split read and the Admin API, however many of them ask at once. The renewal happens
there, once for all of them: a token within five minutes of its expiry is asked for again on the next request, the
session's `reconnect` reads the same threshold and clock, and an explicit reconnect (the window's keep-warm timer,
the probe) renews it for every layer. The sessions of one command share the organization too, asked for once. Under
`--verbose` each acquisition writes one `exchange-online` line to the log (`exchange token acquisition 1: …`, the
minutes it is valid for), never the token.

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

- `mailbox list [--shared]` (task 048): the Admin API's `Get-Mailbox -ResultSize Unlimited` over every page, with
  `$select` on the list's fields, by display name, and no sidecar at all; the endpoint takes no
  `-RecipientTypeDetails` (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-mailbox), so `--shared`
  keeps the shared mailboxes of the whole list. When the run has no Admin API, or the Admin API cannot answer for a
  reason other than the sign-in (not enabled, a refused permission, a service error), the sidecar answers with
  `Get-EXOMailbox -ResultSize Unlimited` (`-RecipientTypeDetails SharedMailbox`). Measured live on 2026-10-30: 621
  mailboxes in 7.5 s, no pwsh started.
- `mailbox access <mailbox>`: FullAccess (`Get-EXOMailboxPermission`), Send As (`Get-EXORecipientPermission
  -AccessRights SendAs`) and Send on Behalf; the direct, allowed grants only, without `NT AUTHORITY\…` entries.
  The mailbox and its Send on Behalf delegates with their display names come from the Admin API (`Get-Mailbox
  -IncludeGrantSendOnBehalfToWithDisplayNames`, asked for again without the parameter, which is in staged rollout,
  when the service refuses it) while the sidecar reads the two rights, side by side; when the Admin API cannot
  answer (a refused permission, not enabled; a missing mailbox is exit code 4 as it is) or the run has none, the
  sidecar reads the mailbox too and resolves the delegates' names with one piped `Get-EXORecipient` (task 016). A
  grant to a group is listed as the group; `--expand-groups` (task 044) resolves the holders with one piped
  `Get-EXORecipient`, and a group among them (a recipient type with `Group` in it) lists its members over every
  nesting level from Graph, each marked direct or nested with the group it comes through (task 041): two rounds of
  `$batch` whatever the number of groups, the direct and the transitive members of every group, then the direct
  members of the groups nested in them. The table puts a group's members under its row with a `through` column; a
  dynamic distribution group, which Graph does not hold, is marked and not expanded. Measured on 2026-10-29 at
  5.5 s for a shared mailbox, sidecar only.
- `mailbox access --user <upn>`: the reverse lookup in four bulk calls whatever the tenant's size: the user as a
  recipient (the names Exchange may use for it in a permission entry), every mailbox with its Send on Behalf list,
  the FullAccess entries of every mailbox (the mailboxes' object ids piped into `Get-EXOMailboxPermission`), and
  the organization's Send As entries. A mailbox whose permissions could not be read makes the answer partial (exit
  code 6). The throttling budget is the module's: the `Get-EXO*` cmdlets back off and retry a throttled request
  themselves, and a request that still fails is exit code 5. The module sends one request per piped mailbox, about
  0.2 s each: over 620 mailboxes the lookup took 138 s, unthrottled (task 016, measured 2026-10-29). So the
  FullAccess entries are read in parallel by default (task 043): the piped mailboxes split into `--sidecars N`
  shares, 2 to 8, 4 by default, each on its own sidecar connected with the same token at once. Measured 2026-10-30
  over 620 mailboxes for an account with 8 grants: piped 122 s, 4 sidecars 41 s, 8 sidecars 37 s, so the floor is
  the service's time per mailbox, about 0.2 s, spread over the sidecars, and 8 buy almost nothing over 4.
  `--method piped` keeps the one sidecar. `Get-EXOMailboxPermission` without an identity, the cmdlet's documented
  default parameter set, would be one listing call, but the module refuses it ("Identity is a mandatory value",
  exit code 13 in 6 s), so it is not offered. With
  `--expand-groups` (task 044) the grants the user holds through a group count too: the user's groups at every
  nesting level from Graph (`/users/{upn}/transitiveMemberOf/microsoft.graph.group`, an advanced query, beside the
  sidecar's reads), matched against the entries' holders by display name, address, alias or object id, and a
  holder no name matched, and no mailbox is, resolved with one piped `Get-EXORecipient` to its object id; such a
  grant names the group (`via`), and the user's group count is in the envelope (`groups`).
- `mailbox forwarders`: every mailbox with `ForwardingSmtpAddress` or `ForwardingAddress` set, whether it keeps a
  copy (`DeliverToMailboxAndForward`), and whether the target is outside the accepted domains (`Get-AcceptedDomain`;
  a `ForwardingAddress` is a recipient, resolved to its address with one piped `Get-EXORecipient`), the external
  ones first. It stays on the sidecar (task 055): the Admin API's `Mailbox` endpoint documents only `Identity`,
  `ResultSize` and `IncludeGrantSendOnBehalfToWithDisplayNames` and answers its core properties only
  (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-mailbox). Measured 2026-10-30 with
  `probe admin-api --forwarding`: a `$select` of the three forwarding fields is honored but drops them (the answer
  carries `Identity` alone), and `Properties` or `PropertySets` in the body is a 400 ("Invalid parameter(s) for
  cmdlet 'Get-Mailbox'"); the sidecar's read took 11.5 s over 620 mailboxes (38 forwarders, 15 external).
- `mailbox size [<mailbox>] [--top N]`: the largest first. Every mailbox's size comes by default from Graph's
  mailbox usage report (task 043: `/reports/getMailboxUsageDetail(period='D7')`, a CSV behind a redirect, a few days
  old, three days on 2026-10-30; `Reports.Read.All` and a reports reader role), joined by user principal name to the
  Admin API's mailbox list (with `$select` on the fields the table shows; the sidecar's list when the run has none),
  both read side by side: no sidecar at all. The footer names the report's day and the mailboxes the report has no
  row for yet; a report row carries the bytes and no `totalItemSize` text, which the table does not show anyway; a
  report whose names are concealed (the tenant's privacy setting for the reports) cannot be joined and is exit code
  9 with the setting in the hint, and without the permission it is exit code 8 naming it. One mailbox, and
  `--method piped`, read the live size: `Get-EXOMailboxStatistics`, the size read from the service's text (`1.2 GB
  (1,288,490,188 bytes)`), a request per mailbox, about 0.1 s each; `--method parallel` splits the piped mailboxes
  across `--sidecars N`. Measured 2026-10-30 over 620 mailboxes, `--top 5`: report 16 s, piped 56 s, 4 sidecars
  35 s, 8 sidecars 37 s. Under `--verbose` the report method writes when each of its two reads answered, timed from
  their common start (task 048): `mailbox size, report method: Graph's usage report answered after … ms` and the same
  line for the Admin API's mailbox list. Measured live on 2026-10-30 over 621 mailboxes: the usage report answered
  after 1.9 s, the Admin API's mailbox list after 6.5 s, the command in 6.5 s with no sidecar, so the list is the
  slow half.
- `mailbox folder-permissions <mailbox> [--folder <path>]` (task 042): `Get-MailboxFolderPermission -Identity
  <mailbox>:\<folder>` through the Admin API. The folder path is the folder's name in the mailbox's own language
  (`:\Calendar` is not found in a Hungarian mailbox, whose calendar is `Naptár`; measured 2026-10-30), and the cmdlet
  documents no language-free form, so without `--folder` the sidecar names the default calendar first
  (`Get-EXOMailboxFolderStatistics -FolderScope Calendar`, the folder whose type is `Calendar`), and the English
  `Calendar` is tried only when that call cannot answer. A given folder (a nested one as `Inbox/Reports`) is read
  through the Admin API alone; a folder not found is exit code 4 with the language in the hint.
- `mailbox group-members <group>` (task 042, the Admin API only): `Get-DistributionGroupMember`, a distribution
  group's direct members as Exchange keeps them, the hidden ones included, over every page.
- `mailbox organization` (task 042, the Admin API only): `Get-OrganizationConfig` (the name and the MailTips
  settings the endpoint documents) and `Get-AcceptedDomain` side by side, the default domain first.

The account needs an Exchange Online role that reads recipients and their permissions (View-Only Organization
Management or Recipient Management; the Exchange Administrator and Global Administrator roles include them); the
app registration needs `Exchange.Manage`, and `Exchange.ManageV2` for the Admin API.

**The Admin API backend** (task 042, `Exchange/AdminApi/`). `AdminApiClient` runs a cmdlet behind the sidecar's
interface (`IExoCmdletRunner`), so a lookup reads either layer's objects the same way: a cmdlet of
`AdminApiCmdlets` (`Get-Mailbox`, `Get-MailboxFolderPermission`, `Get-DistributionGroupMember`,
`Get-OrganizationConfig`, `Get-AcceptedDomain`: the reads of the documented endpoints, any other refused before
anything is sent with exit code 12) becomes `POST https://outlook.office365.com/adminapi/v2.0/<tenant id>/<endpoint>`
with a `{"CmdletInput":{"CmdletName","Parameters"}}` body, `X-AnchorMailbox: UPN:<the signed-in administrator>` (the
delegated flow's key for a call, which task 010 proved in the tenant) and the properties to keep as `$select`;
every page is read by posting the same body to its `@odata.nextLink` (valid 5 to 10 minutes;
https://learn.microsoft.com/exchange/reference/admin-api-get-started). The token is the broker's for
`https://outlook.office365.com/.default`, the one the command's sidecars connect with (the shared token source of
task 049; `HttpAdminApiTransport` sends it to that host only),
and the tenant's id comes with it. A 429 or 503 is retried within the Graph client's budget; a failed answer is
the contract's failure: 401 is 3, 403 is 8, a 404 or a 400 that says the object was not found is 4, throttling 5,
any other 400 a cmdlet failure (13), a server error or no network 9. The undocumented `adminapi/beta/InvokeCommand`
endpoint is never called. `ExchangeBackends` gives a mailbox command its three layers over one sign-in (the
sidecar, the Admin API, Graph, and more sidecars on demand for `--method parallel`), and the envelope's
`meta.backend` names each layer the command used, in the order first used, joined with `+` (`admin-api+pwsh`,
`graph+admin-api`); a command that reads the sidecar alone still says `pwsh`. Which read goes through which layer:

| read | layer |
| --- | --- |
| `mailbox list` | Admin API (the sidecar when it cannot answer) |
| `mailbox forwarders` | sidecar (the Admin API's `Mailbox` endpoint does not carry the forwarding fields; task 055) |
| `mailbox access <mailbox>`: the mailbox and its Send on Behalf delegates by name | Admin API (the sidecar when it cannot answer) |
| `mailbox access <mailbox>`: FullAccess, Send As | sidecar |
| `mailbox access --user`: every mailbox, every FullAccess and Send As entry | sidecar (FullAccess split across 4 by default) |
| `--expand-groups`: the group holders' members, the user's groups | Graph (the holders resolved by the sidecar) |
| `mailbox size` | every mailbox: Graph's usage report and the Admin API's mailbox list; one mailbox or `--method piped`/`parallel`: sidecar |
| `mailbox folder-permissions` | Admin API (without `--folder`, the calendar's name from the sidecar first) |
| `mailbox group-members`, `mailbox organization` | Admin API |
| `mailbox access grant`, `revoke`: the plan's current state | as `mailbox access <mailbox>`, and each holder as a recipient through the sidecar |
| `mailbox forwarding set`, `clear`: the plan's current state | sidecar (the mailbox's forwarding, the accepted domains) |
| every mailbox write: the apply | sidecar (the write cmdlets; the Admin API has no documented write) |
| `group members add`, `remove` on a distribution or mail-enabled security group: the plan's current state | Graph (the group and its type, the users to add), the Admin API's `Get-DistributionGroupMember` (Graph's list when the run has none or it cannot answer) |
| `group members add`, `remove` on a distribution or mail-enabled security group: the apply | sidecar (`Add-`/`Remove-DistributionGroupMember`) |

**Tests and measurements.** `M365AEGIS_EXO_MODULE_ROOT` makes the script look for the module in one folder only,
which the tests point at the fake module under `tests/sidecar/modules` (its made-up mailboxes are listed in
`tests/sidecar/README.md`), and under such a root the script turns pwsh's module auto-loading off, so a cmdlet the
fake module does not export fails instead of being imported from the machine's real module (task 050; a Pester test
holds the fake's exports to the allow-list); `M365AEGIS_EXO_TEST_ORGANIZATION` makes the command line connect to that organization
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
used. `probe admin-api --mailbox <address> [--folder <name>] [--group <address>]` (task 042) also reads that
mailbox with its delegates by name, that folder's permissions and the group's first members, and prints a list's
first entry as its pattern (a run of letters an `a`, of digits a `9`: `a a <a@a.a>`), so the form of an entry the
documentation leaves open shows and its value does not. `--forwarding` (task 055) reads `Get-Mailbox` of `--mailbox`
(else one mailbox) in four ways, a `$select` of the three forwarding fields and the body parameters `Properties`,
`PropertySets: Delivery` and `PropertySets: All` that `Get-EXOMailbox` takes, and prints per way its status and which
forwarding fields came back; `--mailbox-parameters '<JSON object>'` (with `--select <names>`) tries one more body.

## Writes and plans

Every write is a plan first (D-006, tasks 051 to 054): the command reads the current state, compares it with what
was asked for, and lists only what is not so already; nothing is sent until the plan is applied.

**The flags and the exit codes.** Every write command takes `--dry-run` and `--yes`; there is no `--force`. The group
and mailbox writes also take `--resume <record>` (see [A resume](#a-resume-from-a-plan-record) below).

| asked | answer | exit code |
| --- | --- | --- |
| `--dry-run` | the plan (`mode: dry-run`), nothing sent | 0 |
| neither flag | the plan (`mode: refused`) and the `refused-without-yes` error, nothing sent | 7 |
| `--dry-run` and `--yes` | a usage error before anything starts | 2 |
| any, with nothing to change | the plan (`mode: up-to-date`), nothing sent | 0 |
| `--yes`, every change applied | the plan with each result (`mode: applied`) | 0 |
| `--yes`, some changes failed | the same, with the `partial` error | 6 |
| `--yes`, no change applied, every one failing the same way | the same, with that failure (one change refused with 403 is 8) | its code |
| `--resume` with arguments or `--from-csv`, of a raw call, of another write, of another profile, or of a record with every change applied | a usage error before anything starts | 2 |
| `--resume` of a record that does not exist | the `not-found` error before anything starts | 4 |

A plan that cannot be computed (a group, a mailbox, a user or a recipient that does not exist, a read that failed)
is that failure, with nothing sent. The apply sends the changes one after another and keeps going past a change the
service refused; it stops at a failure no later change could pass (the sign-in, exit code 3; the sidecar, 11; the
allow-list, 12; an internal error, 70) and marks the rest `skipped`. Changes that one request can carry together go
together (task 058): the group writes' Graph `$ref` requests, twenty to a `$batch` call, each change's result read from
its own part of the answer, so a part the service refused fails its own change (with the error a single request would
have had, a 403 naming the permission) and the others stay applied; a batch call that fails as a whole fails each of
its changes. A plan of a hundred member changes is five requests. `call` with a method other than GET is a plan of
one change, the request as it is (`action: send`); under `--yes` its answer is the data, as for a GET.

**The `Plan`** (`Core/Operations/Plan.cs`), the envelope's data of every write, and the shape the task card
sketch of task 018 made up, so the window's card reads it: `operation` (stable and kebab-case,
`group-members-add`, `mailbox-access-grant`, `mailbox-forwarding-set`, `call`, …), `title` (one line), `changes`
(each with `action` — `add`, `remove`, `set`, `clear`, `send` —, `target`, `detail`, `current`, `desired`, the CSV
`row` when it comes from a file, and after an apply `result`: `status` `applied`, `failed` with the envelope's
`error`, or `skipped`), `unchanged` (`target`, `detail`, `state`, `row`), `warnings`, `mode` (`dry-run`, `refused`,
`applying`, `applied`, `up-to-date`), after an apply `record`, `started` and `finished`, and under `--resume` the
record it continues (`resumes`). The table is a row per
change and per object already as asked (by the CSV line when the plan comes from a file): the action, the target,
the change, the state now and after, the result (`planned`, `not sent`, `applied`, `failed: <code>`, `skipped`,
`unchanged`); under it the title, the counts, the warnings, each failed change with the service's message, and what
was done with the record's file.

**The plan records.** An apply writes `%LOCALAPPDATA%\m365-aegis\plans\<yyyyMMddTHHmmss.fffZ>-<operation>.json` (UTC,
so the names sort by time; a second apply in the same millisecond gets `-2`): once when it starts (`mode:
applying`, no results) and again when it ends (each change's result, the times), through a temporary file. A record
left at `applying` is an apply that was cut short; a change whose step was running then is `failed` with the code
`internal` or `cancelled`. The record holds `version` (2 since task 065; 1 before), `source` (`command line`, or
`window` for a write card's apply, [below](#the-windows-writes)), `profile`, `arguments` (as
given), the `plan`, and for a batch from a CSV file the file's full path and text (`input`: `file`, `content`), so a
resume replays the lines the apply had (task 062). A dry run, a refusal and a plan with nothing to change write
nothing. `M365AEGIS_HOME` moves the folder with the rest of the home; the tests point it at a temporary one.

**The records read back** (task 060). `plan list` lists the records of the home folder, newest first: when the apply
started (UTC), the operation, the mode (`applied`, or `cut short` for a record left at `applying`), and how many
changes it had, applied, failed and skipped, with the record's file name; a file in the folder that is not a plan
record is counted under the table and left out. `plan show <record>` prints one record (by the file name `plan list`
shows, with or without `.json`, or by its path) as every write prints its plan, each change with its result, and
under it who applied it, when, with which profile and arguments; its envelope's data is the record as the file holds
it. Both read the local files only: no sign-in, no request, backend `local`. A record that does not exist is exit code
4; a file that is not a plan record is a usage error (2).

**In code.** A write operation of the core returns a `WritePlan` (`Core/Operations/WritePlan.cs`): the `Plan` and one
step per change, built with `PlanBuilder`. A step sends its change alone (`PlanStep.Alone`), or is an item of a
`PlanBatch` (`PlanStep.Batched`), which sends the items of up to its size of neighboring changes in one request and
answers one outcome per item (task 058; `GroupWrites.RefBatch` is the Graph `$batch` of the `$ref` writes).
`PlanGuard.RunAsync(plan, mode, store, …)` is the one place a step runs:
it prints, refuses or applies, and writes the records through `PlanStore`, which also reads them back
(`PlanStore.List`, `PlanStore.Open`); `PlanResume` narrows a plan to the changes a record left to do. In the Cli, `WriteFlags` adds the two
flags to a command and `WriteRunner` reads them, runs the plan over `GraphRunner` or `ExchangeRunner` (so the
envelope's `meta.backend` names the layers the plan read and the apply wrote through) and renders the `Plan` with
`PlanTableView`. `WriteFlags` also adds `--resume`, whose option action (`ResumeAction`) runs in place of the
command's own: it reads the record, parses the record's arguments again with this run's flags and runs the command's
own action under the resume, so a write gets `--resume` without code of its own (`call` opts out). A new write adds an
operation to the core that returns a `WritePlan`, and a command whose action is one `WriteRunner` call.

**The writes:**

- `group members add|remove <group> <user…>` and `group owners add|remove <group> <user…>` (task 052,
  `Groups/GroupWrites.cs`): the group is named as for `group members`; the current list is the lookup's. A user to
  add is a user principal name (`/users/{upn}`) or the object id of any directory object (`/directoryObjects/{id}`),
  resolved twenty to a `$batch` call, and one that does not exist is exit code 4 before anything is sent; a user to
  remove who is not on the list is unchanged. A change is `POST /groups/{id}/members/$ref` with
  `{"@odata.id":"https://graph.microsoft.com/v1.0/directoryObjects/{id}"}`, or `DELETE /groups/{id}/members/{id}/$ref`,
  the owners alike (https://learn.microsoft.com/graph/api/group-post-owners), twenty to a `$batch` call (task 058). The
  plan warns when a group would lose its last owner, and when the service will refuse the change: a group Graph keeps
  no owners for, a group synced from on-premises Active Directory. The members of a distribution or mail-enabled
  security group (the group's type is Graph's, read with its name) go through Exchange instead (task 061): the current
  members are the Admin API's `Get-DistributionGroupMember` (the hidden ones included; Graph's list when the run has no
  Admin API or it cannot answer), a user is matched by any address, object id, alias or name Exchange gives the member,
  and each change is `Add-DistributionGroupMember` or `Remove-DistributionGroupMember -Identity <the group's address>
  -Member <the member's address> -BypassSecurityGroupManagerCheck -Confirm:$false` through the sidecar, so the envelope's
  backend is `graph+admin-api` for the plan and `graph+admin-api+pwsh` after an apply. A Microsoft 365 or security
  group's members and owners stay on Graph (backend `graph`), and so does a group synced from
  on-premises Active Directory, with the warning that the service will refuse the change: Exchange Online refuses a
  member write on a synced group too, so the change belongs in the on-premises directory. The owners of a distribution
  or mail-enabled security group (`ManagedBy`) go through Exchange too (task 064, [below](#the-writes-follow-ups-owners-resume-and-prune));
  the members and owners of a dynamic distribution group are not written.
- `mailbox access grant|revoke <mailbox> --user <holder>… --right FullAccess|SendAs|SendOnBehalf…` (task 053):
  `--user` and `--right` repeat, a right also comma-separated, any case, with or without hyphens. The current holders
  are read as `mailbox access <mailbox>` reads them, each holder to change as a recipient (`Get-EXORecipient`, a
  missing one exit code 4), so a grant held already is told by every name Exchange may write in a permission entry.
  The apply runs `Add-MailboxPermission -AccessRights FullAccess -InheritanceType All` or
  `Remove-MailboxPermission` the same way, `Add-RecipientPermission -AccessRights SendAs` or
  `Remove-RecipientPermission`, or `Set-Mailbox -GrantSendOnBehalfTo @{Add=…}` or `@{Remove=…}`. A grant to a group
  warns that every member, the nested ones too, holds it (and that FullAccess through a group is not automapped);
  a grant to a mail contact or a distribution group warns that the service will likely refuse it. The account needs
  an Exchange Online role that changes recipients (Recipient Management or Organization Management).
- `mailbox forwarding set <mailbox> --to <address> [--keep-copy]` and `mailbox forwarding clear <mailbox>` (task 053):
  the current forwarding as `Get-EXOMailbox` carries it; `set` is `Set-Mailbox -ForwardingSmtpAddress <address>
  -DeliverToMailboxAndForward <keep a copy>`, unchanged when the mailbox forwards there already with the same copy
  setting; a `ForwardingAddress` (a recipient), which takes precedence over an SMTP address, is cleared with it and
  the plan warns; an address outside the accepted domains warns that the mail leaves the organization and that the
  outbound spam filter policy blocks automatic external forwarding unless it allows it; forwarding without a copy
  warns too. `clear` sets both to `$null` and `DeliverToMailboxAndForward` to `$false`. A mailbox named twice is a
  usage error.

**A batch from a CSV file** (task 054). `--from-csv <file>` on every write above takes the changes from a file
instead of the arguments (both at once is a usage error): a header line naming the columns, in any case and order,
then one change per line; commas between the cells, or semicolons when the header has semicolons and no comma (a
spreadsheet saved under a locale whose decimal mark is the comma); a cell in double quotes may hold the separator, a
line break and a doubled quote; UTF-8 with or without a byte order mark (a UTF-16 file with a byte order mark, as
Excel's "Unicode Text" writes it, is read too; without one it fails the header check and the whole file is refused);
blank lines are skipped.

| write | columns |
| --- | --- |
| `group members add`, `remove`; `group owners add`, `remove` | `group`, `user` |
| `mailbox access grant`, `revoke` | `mailbox`, `user`, `right` (`FullAccess`, `SendAs`, `SendOnBehalf`) |
| `mailbox access grant` (task 059) | optionally `automapping` (`true`, `false`, `yes`, `no`, `1`, `0`; empty is true; `false` only on a `FullAccess` line) |
| `mailbox forwarding set` | `mailbox`, `to`, and optionally `keepCopy` (`true`, `false`, `yes`, `no`, `1`, `0`; empty is false) |
| `mailbox forwarding clear` | `mailbox` |

The file is read before anything starts, with no sign-in: an unknown or missing column, a line with more or fewer
cells than the header, a stray or unclosed quote, an empty required cell, an unknown right or no line under the
header refuses the whole file with exit code 2 and a message that names the file and the line. The rest is one plan
over every line (a group, a mailbox or a user is read once however many lines name it, a line that repeats an
earlier change is unchanged, and two lines setting one mailbox's forwarding are a usage error), each change and each unchanged object with its line (`row`, the table's `line`
column); a line naming a missing object fails the plan with the line in the message. The apply goes past a failed
line (exit code 6 with each line's result), and the record holds the whole batch.

### The window's writes

The window's write cards (task 082) run the mailbox writes above over the bridge, through the same plan and the same
guard. A write request (`mailbox-access-grant`, `mailbox-access-revoke`, `mailbox-forwarding-set`,
`mailbox-forwarding-clear`; their payloads are the command's arguments, [The front end](#the-front-end)) makes the
rows as the command does and plans them over the window's layers (`MailboxLookups.PlanAccessAsync` or
`PlanForwardingAsync` on the window's `ExchangeBackends`); `PlanGuard.RunAsync` under `WriteMode.DryRun` prints the
plan, so the `plan` reply is the `Plan` `--dry-run` prints (`mode: dry-run`, or `up-to-date`), nothing sent and
nothing recorded. The dispatcher holds a plan with a change under a new id (32 hex digits) with its `PlanStore`;
`apply-plan` with that id and `"confirm": true`, the card's "Confirm and apply", which stands for `--yes`, takes it
out and runs `PlanGuard.RunAsync` under `WriteMode.Apply`: the steps go through the window's warm session (the
sidecar of the plan's `ExchangeBackends` is the window's `WindowExchange`), and the `plan-applied` reply is the
applied plan with each change's result and the record, and the failure the envelope would carry beside it when not
every change was applied (`partial`, or the one failure's own code), each hint in the window's words. Refused, with
nothing sent and the `error` reply: an `apply-plan` without `"confirm": true` (`refused-without-yes`, and the plan
waits for the confirmation), for an id the window does not hold (never planned, applied already, dropped by a
`login` or a `logout`: `usage`), or for a plan older than `BridgeDispatcher.PlanLifetime`, 15 minutes (`usage`;
what it read may have changed). A missing user, right or address is a `usage` error in the card's words before
anything starts. The record is the command line's file under the home folder's `plans` with `source: window`, the
window's profile and as `arguments` the command line that makes the same plan with `--yes` (`mailbox access grant
finance@contoso.com --user bob@contoso.com --right FullAccess --no-automapping --yes`), so `plan show` prints it and
the command line's `--resume` continues an apply the window left partial. Under `--verbose` the plan writes its
`window:` line as a lookup does, with the counts (`1 change(s) planned, 0 unchanged, 1 warning(s)`), and the apply
one of its own (`apply-plan answered in … ms (mailbox-forwarding-set: 1 applied, 0 failed, 0 skipped); exchange
session 1, …`, or `(the apply stopped)` when it was cancelled), never a name. Group writes, CSV batches and a resume
stay on the command line.

### A resume from a plan record

`--resume <record>` on a group or mailbox write (task 062, `Core/Operations/PlanResume.cs`) continues an apply that
ended partial, stopped at a failure no later change could pass, or was cut short: the record names the changes to
continue, the ones it left `failed` or `skipped` (or without a result, for a record left at `applying`). The command
line of the record is parsed again (its arguments; a CSV file from the text the record keeps, never from the file,
which may have changed since) with this run's `--dry-run` or `--yes` and output flags, and the write plans its
original changes against the current state; of that plan, only what continues a change the record left to do is kept
(by its CSV line when the record has one, else by the action, the target and the change): a change still to make is
planned again, one made in the meantime comes out unchanged, and one the record applied is left out whatever its state
now. A change the record left to do that the new plan does not name becomes a warning. The guard is the usual one, and
the new record names the one it continues (`resumes`) and keeps the original arguments and file, so a resume can be
resumed. A record of two failed and three skipped changes resumes as a plan of at most five. The record is named as
for `plan show`; it must be of the same write (the hint names the command that wrote it), of the same profile, and not
of a raw `call`; a record with every change applied has nothing to resume. Arguments or `--from-csv` next to
`--resume` are a usage error: the record names the changes.

### The writes follow-ups: owners, resume and prune

Tasks 064 to 066, the writes follow-ups bundle of 2026-10-30.

**The owners of a distribution or mail-enabled security group** (task 064, a D-004 note of 2026-10-30). `group owners
add|remove` on such a group (not synced; its type is Graph's, read with its name) plans against the group's `ManagedBy`
and applies through the sidecar, the way task 061 routes its members:

| step | layer |
| --- | --- |
| the group and its type, the users to add | Graph (as every group write) |
| the current owners | sidecar: `Get-DistributionGroup -Identity <the group's address>` (`ManagedBy`, the owners by each recipient's name), the names resolved with one piped `Get-EXORecipient`; a name no recipient answers to (a role group) is listed as it is |
| each change | sidecar: `Set-DistributionGroup -Identity <the group's address> -ManagedBy @{Add=<address>}` or `@{Remove=<address>}` `-BypassSecurityGroupManagerCheck -Confirm:$false` |

The Admin API documents no endpoint that reads a group's settings (its six endpoints are `AcceptedDomain`,
`DistributionGroupMember`, `DynamicDistributionGroupMember`, `Mailbox`, `MailboxFolderPermission` and
`OrganizationConfig`; https://learn.microsoft.com/exchange/reference/admin-api-endpoints-reference), so `ManagedBy` is the
sidecar's read and the envelope's backend is `graph+pwsh`. The allow-list admits `Get-DistributionGroup` (a read) and
`Set-DistributionGroup` with `Identity`, `ManagedBy`, `BypassSecurityGroupManagerCheck` and `Confirm` only
(`ExoCmdlets.WriteParameters` and the script's `$script:WriteParameters`): the cmdlet can change any setting of a
group, and the list keeps it to the owners. A user is matched against the owners by any address, object id, alias or
name Exchange gives the recipient; the plan warns when the group would lose its last owner. A Microsoft 365 or
security group's owners stay on Graph, a synced group's too with the warning that the service will refuse the change,
and over Graph alone (no Exchange layers) a distribution group's owners keep the warning that Graph keeps none. The
owner of a distribution group must be a mailbox, a mail user or a mail-enabled security group; another one is the
service's refusal of that change (exit code 13 for it, 6 for a partial apply).

**A resume over the pending lines, by object ids** (task 065). A record of version 2 numbers each change, and each
object already as asked, by its row's place among the write's rows (`index`, from 1: the n-th change asked for, from
the arguments or the CSV file's lines) and keys it by object ids (`key`: `<group id>/<user id>` for a group write,
`<mailbox id>/<right>/<holder id>` for a mailbox right, `<mailbox id>` for the forwarding); both are in the plan's
envelope too. Under `--resume` the write reads its rows from the record as before, then keeps only the rows of the
changes the record left to do (`PlanResume.Select`, called by `WriteRunner.TryRows`, so a write needs no code of its
own) before it plans: 60 pending lines of 100 read 60 objects, and a line applied before whose object was deleted
since is not read again. The new plan's changes are matched to the pending ones by the action and the object ids
first, then by the CSV line, then by the text, so a group or a user renamed since the apply still matches. A record
of version 1, or one whose pending changes carry no row's place, plans every row again and matches by the line and
the text, as before.

**The plan records' size, and `plan prune`** (task 066). `plan list` also gives `count` (the records) and `bytes` (every
file in the folder, the records and any other); the table's footer says both. `plan prune --older-than <days>`
removes the records of the applies started more than that many days ago (their `started`, else the file's time): it is a
write like every other, local only, a plan of one change per record (`action: remove`, the file name as the target,
the operation, the start, the changes and the size as the change), printed by `--dry-run`, refused without `--yes`
(exit code 7), up to date with nothing older; the apply records itself like any write (`plan-prune`), and that record
is new, so it stays. `--older-than` is required and at least 1, so a record of an apply running now is never removed
(2 otherwise); a file that is not a plan record is never removed; a record that left changes to do warns that its
resume goes with it; a file the file system refuses to remove fails its change with exit code 8 and the others go on.
`--resume` does not apply to it. The folder has no automatic size limit: a limit enforced at apply time would remove
records without a plan anyone confirmed, which D-006 rules out for every write, and the records are the audit trail
and the resume's source; `plan list` shows the size, and the prune is the administrator's.

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

The messages (tasks 017, 040, 045 and 047; `BridgeMessageType` in C#, `MessageType` in TypeScript, one sample each under
`tests/contracts/bridge`): the page sends the `ready` notification, then

- `ping`, answered with `pong` (the product and the version);
- `whoami`, answered with `signed-in` (the core's `SignInInfo`) or `signed-out` (the profile it was tried for, why
  and what to do);
- `login`, with `{"deviceCode": true}` or no payload (task 040): `AuthService.LoginAsync` for the window's profile,
  silently when it can, else the broker's account picker over the window, else (or when asked) a device code,
  answered with `signed-in`. While a device-code sign-in waits for the browser step, the host sends the
  `device-code` notification (no id: the page to open, the code, the service's sentence, the expiry), which the
  sign-in card shows with a copy button and an "Open in browser" button (task 047); the page itself opens no other
  site. A profile is never written from the window: without one, `login` is the `auth` error whose hint sends to the
  administrator (the window's hints, below), and the card offers no "Sign in" until it exists;
- `open-sign-in-page`, no payload (task 047): while a device-code sign-in waits for its browser step, the host hands
  the page its own `device-code` notification carried to the default browser and answers `sign-in-page-opened` (the
  page). The front end never names an address: without a sign-in waiting the request is the `usage` error, a page
  off Microsoft's sign-in hosts is never opened (`service-error`), and the host's `OpenInBrowser` (below) is its one
  use;
- `logout`, answered with `logged-out` (the core's `SignOutInfo`); it also ends the window's Exchange Online
  session, whose sidecar is connected with the account's token;
- `group-members` with `{"group": "<name, address, id or word>"}`, answered with `members` (the core's
  `GroupMembers`);
- `mailbox-list` with `{"shared": true}` or no payload, answered with `mailboxes` (the core's `MailboxList`), and
  `mailbox-access` with `{"mailbox": "<address, name or id>"}` or `{"user": "<upn>"}` (one of the two), answered
  with `mailbox-holders` (`MailboxAccess`) or `user-grants` (`UserAccess`) (task 045). Since task 056 the list
  comes from the Admin API, as `mailbox list` does, a mailbox's access since task 063 from the Admin API beside the
  window's sidecar session, and a user's since task 077 split across several sidecars as `mailbox access --user`
  reads it (below). A partial answer of the
  reverse lookup (exit code 6 on the command line) is the `user-grants` reply, whose `failed` list the page shows as
  a warning, as the envelope carries the data with the error;
- the write requests (task 082, [The window's writes](#the-windows-writes)), marked `[BridgeWrite]` in
  `BridgeMessageType` (its `Writes` lists them; `writeRequests` in TypeScript): `mailbox-access-grant` and
  `mailbox-access-revoke` with `{"mailbox": …, "users": […], "rights": ["full-access", "send-as",
  "send-on-behalf"], "noAutoMapping": true}` (a grant's only), `mailbox-forwarding-set` with `{"mailbox": …, "to":
  …, "keepCopy": true}` and `mailbox-forwarding-clear` with `{"mailbox": …}`, each answered with `plan` (`planId`,
  none when there is nothing to change, and the `Plan`), and `apply-plan` with `{"planId": …, "confirm": true}`,
  answered with `plan-applied` (the applied `plan` and, when not every change was applied, its `error`).

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

**The window's hints** (task 046, `Auth/SignInHints.cs`): a failed sign-in's hint comes from the hint set of its
surface. `SignInHints.CommandLine` names the commands (`run m365-aegis login`, the `login --tenant …` line) and is the
default of `AuthService`, `SignInFlow` and `ProfileStore`; `BridgeServices.ForWindow` makes the window's
`AuthService` with `SignInHints.Window`, which names the sign-in card's buttons ("choose Sign in…", "Sign in with a
code") or, for a missing or broken tenant profile, which only the command line writes, the administrator. A
failure's kind and sentence are the same on both surfaces, so the silent token of a lookup carries the window's hint
too; the card shows the hint under every signed-out state, and an `error` reply's toast shows it as its description.
The failures that reach the window after the sign-in (task 057: Graph's, the Admin API's, the sidecar's, the
dispatcher's own and an internal error) keep their kind and sentence, and their hint goes through
`SignInHints.ForSurface`: a hint that names something only a shell runs (`SignInHints.NamesACommand`: a command in
backticks such as `` `m365-aegis doctor` ``, a flag, an `Install-Module` or `winget` line, an `M365AEGIS_` variable, "run
the command") is replaced by the window's hint of the failure's kind (`SignInHints.Window.Failures`: one for every
kind of the contract, such as "the Exchange Online tools on this computer need a check; ask the administrator" for
`sidecar-unavailable`, and the sign-in card's "Sign in" for `auth`), and a hint written for a person stays (a group
lookup's "give the exact display name…", the log file of an internal error). The dispatcher sends every `error`
reply through it; the command line's set has no such hints, so its envelopes and snapshots are unchanged. A Core test
holds the window's set complete over the failure kinds and every failure the core's layers make free of a command.

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
disposes it after the message loop ends, which sends the sidecar `exit` (the script disconnects).

**The window's layers** (task 056): the same holder gives each mailbox request the command line's `ExchangeBackends`:
the session as the sidecar, an Admin API client and Graph, the session and the Admin API over one
`ExchangeTokenSource` for the window (task 049), so the connect, the keep-warm renewals and the Admin API's calls
share one token and its renewal. `mailbox-list` answers from the Admin API's mailbox list with no sidecar start (the
session when the Admin API cannot answer, or in the sidecar-only offline tests, which have no Admin API);
`mailbox-access` reads a mailbox's access over both layers (task 063) and a user's split across sidecars (task 077),
both below. Since task 077 the request's `ExchangeBackends` can also start more sidecars, each a session made the way
the holder makes its own, over the window's token source; they end with the request, and the warm session stays the
window's one. `BridgeServices.ForWindow` makes one set of session options per token source, so the warm session and
the extra sidecars ask for the organization once, as one command line run does. `logout` drops the token source with
the session.
Under `--verbose` each mailbox request writes one `window:` line to the log: the time, the layers the request used
and what the session did, never a token (`mailbox-list answered in … ms through admin-api; exchange no sidecar,
token acquisitions 1`, then `mailbox-access answered in … ms through pwsh; exchange session 1, sidecar starts 1,
renewals 0, last connect … ms, token acquisitions 1`).

**The window's mailbox access over the layers** (task 063): `mailbox-access` with `{"mailbox": …}` runs the command
line's `MailboxLookups.AccessAsync(backends, …)` over the window's `ExchangeBackends`, so the card shows the rows
`mailbox access <mailbox>` prints: the mailbox and its Send on Behalf delegates with their display names from the
Admin API (`Get-Mailbox -IncludeGrantSendOnBehalfToWithDisplayNames`) while the warm session reads FullAccess and
Send As, side by side over the window's one token; the session alone when the window has no Admin API or the Admin
API cannot answer for that mailbox (a missing mailbox is the `not-found` error either way). The result names its
layers: `mailboxLayer` (`admin-api`, or `pwsh` after the fallback) and each holder's `layer` (`admin-api` for a
delegate the Admin API listed, `pwsh` for the rest), written by the layered read only, so the command line's
`--json` carries them too and the sidecar-only lookup's envelope is unchanged. The card shows a "Read through"
column (Admin API or PowerShell) and one line under the mailbox on what each layer read. Under `--verbose` the
request's `window:` line names what each layer read, by counts only: `mailbox-access answered in … ms through
admin-api+pwsh (the mailbox and 1 Send on Behalf delegate(s) from admin-api, 2 FullAccess and Send As grant(s) from
pwsh); exchange session 1, sidecar starts 1, …`, or `(the Admin API could not answer: the mailbox, … from pwsh)` or
`(no Admin API in this window: …)`; a fallback also writes the core's own `admin-api:` line with the service's
code. The bridge sample `mailbox-holders.json` is the Admin API answer for Finance asked by its address, as the
card's pick sends it.

**The window's user lookup over the split read** (task 077): `mailbox-access` with `{"user": …}`, the reverse
lookup, runs the command line's `MailboxLookups.AccessForUserAsync(backends, …, new UserAccessOptions())`, the path
`mailbox access --user` takes with its defaults: the FullAccess entries of every mailbox read `parallel` over four
sidecars (task 043), the first share on the window's warm session and the others on sidecars started for the request
over the window's one token, which end with the request. Before it, one bulk read on the warm session took 262 s over
620 mailboxes in the window (2026-10-30) against the command line's 41 s. The `user-grants` reply carries
`"method": "parallel"`, as the command line's `--json` does. Under `--verbose` the request's `window:` line names the
sidecars by counts only: `mailbox-access answered in … ms through pwsh (FullAccess of 620 mailbox(es) read parallel
over 4 sidecar(s): the window's session and 3 started for this request); exchange session 1, sidecar starts 1, …`;
`exchange` still describes the warm session alone. A mailbox's access (above) starts no extra sidecar.

The page (`src/App.tsx`, `src/components/`): the sign-in card (its state machine in
`components/sign-in-state.ts`: `whoami` reads it, `login` waits with the device code when one comes, `logout`
reads it again; "Sign in" and "Sign in with a code" when signed out with a profile, with the window's hint for the
state, the device code with "Copy the code" and "Open in browser" while it waits, "Sign out" when signed in), the
group members card with the table, the mailbox access card (a mailbox or a user, the shared mailboxes to pick
from, the holders or the grants in a table, each holder with the layer that read it, the partial warning, and the
time of the answer under the table), the write cards (task 082, `components/mailbox-write-cards.tsx`: "Mailbox
rights" and "Mailbox forwarding"), and a toast (sonner) for every `error` reply of the other cards.

**The write cards** follow the sketch below over the core's real `Plan`: Pick › Plan › Confirm › Result, the flow a
reducer in `components/write-flow.ts` and its parts in `components/plan-view.tsx`. "See the plan" sends the write
request once every pick has a value; the plan shows each change (its action, the target, what changes, the state now
→ after), what is left as it is, and the warnings, and a plan with nothing to change offers no confirmation.
"Continue to confirm" lists the changes again with a checkbox ("I checked the plan; make these changes."), and
"Confirm and apply", the one button that sends a write, sends `apply-plan` with `"confirm": true`. "Back to the plan"
and "Change the picks" send nothing (the host's plan runs out after 15 minutes). The result says "Done", "Partly done"
or "Not done", each change with its result and the service's sentence, and the plan record's file. An `error` reply
shows on the card: "Refused: nothing was changed" for a plan the host could not compute or an apply it refused,
"Stopped: check the mailbox before you try again" for an apply that broke off otherwise (an internal error, no answer
in time). The cards wait for a sign-in, and an open plan goes back to the picks at a sign-out. The browser mock
(`src/bridge/mock-writes.ts`) plans the writes over its made-up holders and forwarding and holds them as the host
does; its apply changes nothing and fails a Send on Behalf change on Reception, so a partial result shows.

**The task card design sketch** (task 018, `src/sketch/`): the card list (grant FullAccess on a shared mailbox, add
a group owner, check a user's access), one card's steps (pick, the plan, the confirmation, the result) and the
states (signed out, working, refused, partial), light and dark, in the shadcn/ui components, over made-up data and
without a bridge request: `task-card-flow.ts` is the flow's state machine (tested with Vitest), `sketch-data.ts`
the made-up core, whose plan shape is made up and marked so on the page (the write cards above show the real `Plan`
in the same steps, task 082), and
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
- `IUiHost.OpenInBrowser` (task 047) hands a device-code sign-in's page to the default browser through the shell
  (`Process.Start` with `UseShellExecute`, the address its one argument), outside the WebView2, which still navigates
  nowhere else. Its one use is the `open-sign-in-page` request, and it opens only what `Bridge/SignInPage.cs` allows:
  https on the default port, no user info, and the host `microsoft.com`, `www.microsoft.com`,
  `login.microsoftonline.com` or `login.microsoft.com`; the dispatcher checks it first and the host again.
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

The test-only packages of the coverage (task 070): `@vitest/coverage-v8` (Vitest's V8 coverage provider, for the
100% thresholds of `npm run test:coverage` and the CI report; Vitest measures nothing without a provider package, and
this one is its own, pinned to the Vitest version) and `happy-dom` (a DOM for the component tests, which a file opts
into with `// @vitest-environment happy-dom`: the cards, the page, the entry and the sketch need clicks, input and
effects to reach every branch; there was no DOM before, and the tests drive React 19's own `act` through a small
helper, `src/test/dom.ts`, so no testing library is added). Both are dev packages under MIT and never ship.

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
under `--dry-run` and applies under `--yes`; the window shows the same plan and asks for a confirmation (see
[Writes and plans](#writes-and-plans) for the exit codes of an apply).

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
| `home-access` | no | no account but the user, `SYSTEM` and `Administrators` holds a read right on the home folder (the list a home the product creates has) | another account does, explicitly or inherited, named with its right, with the `icacls` line that gives the folder the product's own list; or the list is unreadable (`icacls "<home>"` shows it) | none; skipped off Windows and while the home does not exist |
| `dotnet-runtime` | no | the runtime the process runs on (a fact, so it has no warn or fail) | | |
| `dotnet-desktop-runtime` | no | the .NET Desktop Runtime the window needs, in the major version of the .NET Runtime (the `Microsoft.WindowsDesktop.App` folders of the same installation) | a patch behind the .NET Runtime (`winget upgrade Microsoft.DotNet.DesktopRuntime.10`) | not installed: the window cannot start (`winget install Microsoft.DotNet.DesktopRuntime.10`); skipped off Windows |
| `pwsh` | yes | 7.6 or later on PATH | the version is unreadable | missing (`winget install Microsoft.PowerShell`) or older |
| `exchange-online-module` | no | 3.10 or later | older (`Update-Module`) | not installed (`Install-Module … -Scope CurrentUser`); skipped without pwsh |
| `sidecar-script` | yes | the SHA-256 of `sidecar\aegis-exo-sidecar.ps1` matches the copy compiled into the Core | differs in its line endings only | missing or changed (reinstall the release zip) |
| `signature` | no | every product file next to the running exe (`m365-aegis*.exe`, `m365-aegis*.dll`, `M365Aegis.*.dll`) is signed by a certificate this machine trusts, with the signer | one is not signed (a build from source, an unsigned zip), or its certificate is not trusted here (an untrusted or distrusted root, expired without a timestamp), each with its `WinVerifyTrust` code | a signature does not hold: the file changed after it was signed (reinstall the release zip); skipped off Windows and without a product file |
| `webview2-runtime` | no | the `pv` of the EdgeUpdate client key, 120 or later | older (Edge Update is blocked) | not installed (`winget install Microsoft.EdgeWebView2Runtime`); skipped off Windows |
| `config` | no | the profiles and the default | no file (the login command), no profile, or no default (`profile use`) | the file does not parse, or a profile lacks a value |
| `device` | no | Entra-joined with a primary refresh token | registered only, or joined without a primary refresh token (task 013: the silent sign-in as the Windows account fails, the profile's account signs in instead), or `dsregcmd` unreadable | neither joined nor registered (Settings > Accounts > Access work or school) |
| `sign-in` | no | the silent sign-in of `whoami`: the account and each token's expiry | no profile, or a resource without a token (the consent) | the silent sign-in failed (`login`) |
| `endpoint-login`, `endpoint-graph`, `endpoint-exchange` | yes | any HTTP answer from `login.microsoftonline.com`, `graph.microsoft.com`, `outlook.office365.com` | a proxy asks for authentication (407), or slower than 3 s | no answer within 10 s (the network, the proxy, the firewall) |

The pwsh and module checks are one pwsh call; the device check reads `AzureAdJoined`, `WorkplaceJoined` and
`AzureAdPrt` of `dsregcmd /status` and nothing that names the tenant. Everything a check reads comes through
`DoctorEnvironment` (`Diagnostics/DoctorEnvironment.cs`), which the tests replace part by part: the home folder's
access list, the pwsh call, the registry, `dsregcmd` (the made-up outputs under `tests/fixtures/doctor/dsregcmd-*.txt`),
the installed Desktop Runtime versions, the two copies of the script, the signatures, the sign-in and the endpoint probe.

The `signature` check (task 085) asks `WinVerifyTrust` (the Authenticode policy, `Diagnostics/Authenticode.cs`)
about each product file in `AppContext.BaseDirectory`, the same names `scripts/sign-release.ps1` signs, with no
revocation check and only the local caches (no download), so `--offline` stays offline, and reads the signer's subject
from the state it leaves. A signature that holds over an untrusted root is a warning with the signer named, since
the team's machines may trust a self-signed certificate by hand; the expected publisher is not pinned yet (it waits
for the certificate decision). The interop is the one part excluded from the coverage ([Coverage](#coverage)); the
names and the verdict of each `WinVerifyTrust` code are tested, and `tests/release/sign-release.Tests.ps1` runs the
doctor of a signed and of a changed folder in the `windows` job.

The `home-access` check (tasks 078 and 088) reads the home folder's access list by SID: an allow entry that carries a
read right (list the folder, or the generic read or all right of an inheritable entry) for any account but the
running user (by its SID), `SYSTEM` (`S-1-5-18`) and `Administrators` (`S-1-5-32-544`) is a warning, whether it is
explicit or inherited; a deny entry is not weighed against it. The detail names each such account once, with the
strongest of `full control`, `modify` and `read` its entries add up to and `inherited` when no entry of it is explicit:
`Everyone` (`S-1-1-0`), `Users` (`S-1-5-32-545`) and `Authenticated Users` (`S-1-5-11`) by these names whatever the
Windows display language (then every account of the machine can read the home), any other by its account name (a
sandbox group, another user, a service), or by its SID when it does not resolve (a deleted account). The hint is one
command line, the same in pwsh and in cmd, that gives the folder the list of a home the product creates
([The safety tests](#the-safety-tests)): `icacls "<home>" /inheritance:r /grant:r "<the user>:(OI)(CI)F"
"*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11`, with every other account
that has an explicit entry added to `/remove:g`; an inherited one goes with `/inheritance:r` and is not named there,
since icacls refuses a SID that does not resolve (error 1332). The doctor never changes the list itself. The check
looks at the folder; the files below it inherit from it.

## The safety tests

Five tests hold the scope of [SECURITY.md](../SECURITY.md) and the output contract together, each over the whole
product rather than one command (tasks 071 to 075, the sixth round of 2026-10-30).

**The exit codes** (task 073, `tests/M365Aegis.Core.Tests/Output/ExitCodeContractTests.cs`). The `ExitCode` enum is
a Verify snapshot of `name = value` lines in value order (`Snapshots/ExitCodeContractTests.TheExitCodes.verified.txt`),
and the exit code table of [The output contract](#the-output-contract) is read back by the test and compared with
the enum and with each `FailureKind`'s error code, both ways: a renumbered or renamed member, a code the table lacks
or a row the enum does not have fails. The table's shape is therefore part of the contract too: one row per code, the
number, the error code in backticks (an empty cell for 0) and the meaning. A new exit code changes the snapshot, the
table and the `FailureKind` in the same change.

**The write guard** (task 071, `tests/M365Aegis.Cli.Tests/WriteGuardTests.cs`). The test walks the command line's
own tree (`Program.Build()`) and takes every command that carries `--dry-run` and `--yes` as a write: today `call`,
`group members|owners add|remove`, `mailbox access grant|revoke`, `mailbox forwarding set|clear` and `plan prune`
(a write of local files: its home starts with three sample records, and all of them must still be there). Each one
runs as a child process without `--yes` (no flag, then `--dry-run`) from its arguments, from its `--from-csv` sample
and from `--resume` over a record cut short while applying that plan, and `call` with each of POST, PATCH, PUT and DELETE; the
recorded Graph and Admin API answers and the fake Exchange Online module answer at once. A run passes when the plan is
the answer (exit 7 and `refused`, or 0 and `dry-run` or `up-to-date`), no record is written, and nothing but reads was
sent: the fixture transport appends every request to the file `M365AEGIS_FIXTURE_REQUESTS` names (one JSON line, the
method, the URL and the body), where only a GET, a `$batch` of GETs and an Admin API call of a `Get-` cmdlet may
appear, and the fake module's write file (`M365AEGIS_FAKE_EXO_WRITES`) stays empty. Every write command also runs
without its inputs, which needs no table line. A new write command joins the enumeration by itself and fails
`EveryWriteCommandHasItsInputs` until it has a line in the test's table of inputs (its arguments, its CSV sample), so
it cannot land without the guard test.

**The window's write guard** (task 079, `tests/M365Aegis.Core.Tests/BridgeWriteGuardTests.cs`). The window's
counterpart of the test above takes its writes from the protocol: every type of `BridgeMessageType` marked
`[BridgeWrite]` (`BridgeMessageType.Writes`), each planned from its sample under `tests/contracts/bridge` through the
dispatcher of a window over every layer at once (`BridgeWindow`: the recorded Graph and Admin API answers, whose
fixture transports write each request to a log, the fake module, which writes each write cmdlet to a file, a
temporary home). The plan must have a change; then the same write is planned again, and every `apply-plan` that is
not a confirmed apply of a plan the window holds is sent (no `confirm`, `false`, the string `"true"`, `1`, no id, the
id in upper case, another id, no payload, other field names, a plan older than its lifetime, one a `logout` dropped):
each is an `error`, only reads reached a layer (the request log read as the command line's test reads it,
`tests/shared/SentRequests.cs`) and the `plans` folder holds no record. Beside it every message type of the protocol
must have its sample named after it, and every sample is sent once: a request answered with `plan` must be marked,
and nothing may reach a layer. A new write request therefore joins the test with its mark and its sample. Taking the
mark off `mailbox-forwarding-clear` turned the sweep red ("answers with a plan but is not marked"), and an apply
hidden behind a correct refusal turned all four writes red on the fake module's file (2026-10-30).

**The CSV edge cases** (task 074, `tests/M365Aegis.Core.Tests/Operations/CsvEdgeCaseTests.cs` and
`tests/M365Aegis.Cli.Tests/CsvEdgeCaseCommandTests.cs`). They pin what [A batch from a CSV file](#writes-and-plans)
promises, over one file per case for each set of columns under `tests/fixtures/csv/edge-cases/<family>/<case>.csv`
(`group`, `mailbox-access`, `forwarding-set`, `forwarding-clear`). Every write's reader (the group writes', grant's,
revoke's, forwarding set's and clear's) reads every case in process; every write that takes `--from-csv` (found in the
tree, as the guard test finds it) runs three of them as a child process under `--dry-run`. What the tests hold: a file
with a UTF-8 byte order mark and CRLF line endings (Excel's "CSV UTF-8") and one with LF read alike; a cell in double
quotes holds a comma; spaces around a cell, a quoted cell and a header's name are trimmed; the header's names match in
any case and order; blank lines at the end (and lines of nothing but spaces) are skipped; a line that repeats an
earlier one is planned once, the repeat listed unchanged with its line ("named twice: the change is planned once"),
while for `mailbox forwarding set` and `clear` it is a usage error naming both lines (`duplicate-line.csv: alice is
named twice (lines 2 and 3)`); a malformed last line refuses the whole file with exit code 2 and its line number
(`malformed-last-line.csv, line 4: 1 cell(s), the header has 2`), with the backend `local`, nothing sent.

**Token hygiene** (task 072, `tests/M365Aegis.Cli.Tests/TokenHygieneTests.cs`, and one Pester case in
`tests/sidecar/sidecar.Tests.ps1`; the window's path since task 080,
`tests/M365Aegis.Core.Tests/BridgeTokenHygieneTests.cs`; the marked broker of both is
`tests/shared/MarkedTokenBroker.cs`). The marker convention: every token of a run carries a marker unique to it
(`aegismarker` and a fresh GUID), the Graph token in a JWT's shape as the service issues it and the Exchange Online
token opaque, so a sink passes only by never taking it, not by a pattern that happens to scrub it. The test runs in
process through the product's own parts with the test's token source: `AuthService` over a broker that hands out the
marked tokens (`login` writes the profile, `whoami`), the sign-in library's verbose log as `MsalTokenBroker.ToSink`
feeds the home's `LogSink` (a bearer header, a query string, a JSON body and a bare JWT, and a line marked as PII),
`call PATCH --yes` over `HttpGraphTransport` and a handler that answers like Graph (one applied, one refused with
403, a GET that cannot reach the service; the handler checks the token went out as the bearer of each), an internal
error whose message carries the header, and the sidecar over the fake module (connected with the token, then
refused with the token in the module's message). Each answer goes through `CommandRunner` in both renderings, and
then every file under the temporary home (`config.json`, `logs\`, `plans\`) and both streams are searched for the
marker: none may hold it, and a leak is named by its sink. The Pester case connects the sidecar with a marked token,
reads, and connects again to a refused organization, and searches the protocol lines and stderr.
The window's run (task 080) goes through `BridgeDispatcher` the same way, under `--verbose` (the home's `LogSink` as
the window's verbose sink): `whoami`, `login` with the account picker and with a device code (its `device-code`
notification), a `group-members` whose request over `HttpGraphTransport` fails with the bearer header in the
exception's message (the `internal` error reply, the stack trace in the log), `mailbox-list` from the Admin API,
`mailbox-access` for a user (the split read: three sidecars connect with the marked token) and for a mailbox (both
layers), a `mailbox-access-grant` and its confirmed `apply-plan` (task 082: the apply over the marked session, its plan
record under the home), all over a `WindowExchange` on `ExchangeTokenSource.ForProfile`, a `mailbox-list` on a window whose sidecar
the fake module refuses with the token in its message (an `error` reply), and `logout`. Then every file under the
temporary home (the `window:` lines and the internal error included), every reply and notification as the host
writes it to the page, and the window's stderr (`Console.Error`, captured for the run) are searched for the marker.
Putting the Exchange Online token into a reply's payload or into the `window:` line on purpose turned it red, naming
the replies and the log file (2026-10-30). A new sink of the product (a file under the home, a stream, a bridge
message) is covered by the search as it is; a new place a token flows through joins the run with its own step.

**The home folder's access list** (task 075, `Config/AegisHome.cs`, `tests/M365Aegis.Core.Tests/Config/HomeFolderAclTests.cs`).
The rule: every writer under the home creates it through `AegisHome.Create(home)` first (the profile store, the MSAL
cache through `MsalTokenBroker.CreateCacheAsync`, the log, the plan store, and the window before WebView2 makes its
data folder), which creates a missing home on Windows with an explicit, protected access list: full control for the
current user (also its owner), SYSTEM and Administrators, inherited by every folder and file below, and nothing
inherited from the parent. `%LOCALAPPDATA%` has that list by default; a home `M365AEGIS_HOME` puts elsewhere (under a
folder at a drive's root, where `Users` and `Authenticated Users` may read) would otherwise inherit its parent's. A home
that exists already is left as it is: the folder is the user's. Off Windows the folder is created plainly (a Unix mode
is not in scope). The test, Windows-only (skipped elsewhere with the reason, so the `dotnet` job skips it and the
`windows` job runs it), creates a parent whose list lets `Users` and `Authenticated Users` read everything below it,
writes the MSAL cache, `config.json`, a plan record and a log line through the product into a home under it, and holds
the home and every entry in it to allow entries for the user, SYSTEM and Administrators only, owned by the user (or
Administrators, elevated); the cache's bytes hold no token in clear (DPAPI) while the product's own reader gets it
back. Without the protected list the same test fails on the inherited `Users` and `Authenticated Users` entries.
The doctor's `home-access` check (task 078, [The doctor](#the-doctor)) is where a user learns that an existing home
does not have the list: the same tests hold it `pass` on a home the product created and `warn` on one that inherits
the parent's `Users` and `Authenticated Users` entries, or a sandbox group's read and a service account's modify (task
088), run its `icacls` hint, and find the home the user's own after it.

## CI and releases

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, under a minutes budget (D-005).
Its first job, `changes`, diffs the change (a pull request against its merge base, a push against the commit
before it) and sets a `code` output: when every changed path is under `docs/`, `tasks/` or `.claude/memory/`, or
is `CHANGELOG.md` or a root markdown file, the five build jobs below are skipped and the run is green in under a
minute; `lint`, `workflow-lint` and `commit-rules` run on every change, and a tag, a manual run or an unknown
base is always the full run (task 023). The skip is a job-level `if:`, never a path filter on the trigger: a
required check whose workflow never starts stays pending and blocks the merge, while a job skipped by its
condition passes its check. The jobs: `dotnet` (locked restore, `dotnet format --verify-no-changes`, the build with the analyzers, the
Core and Cli tests under coverlet and the coverage gate, see [Coverage](#coverage)), `deps` (the vulnerability script, `nuget-license` against `licenses/allowed.json` over the
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
`npm audit` and the front end's license script), `frontend` (Biome, the type check, Vitest with its coverage
thresholds, the Vite build), `sidecar` (PSScriptAnalyzer and the Pester tests against the fake module, with the
runner's own pwsh, and the coverage of the sidecar script), all four skipped on a docs-only change, `lint` (the changelog check, skipped on a tag, see [The changelog](#the-changelog); the fixture check; the
commit-rules self-test) and `workflow-lint` (zizmor over the workflows and `dependabot.yml`; a finding accepted on
purpose carries `# zizmor: ignore[<audit>]` on the step's name line with its reason in the comment above). The
one Windows job, `windows`, runs on every push to `main`, every pull request and every tag (task 068), and not on a
docs-only change: the full build
with the front end, the tests,
`m365-aegis --version` and `doctor --offline`, the window's `--smoke` run (the page loads and the bridge sends
`ready`), the signing tests (`tests/release`, Pester) and the release script's dry run, signed with a throwaway
certificate the step makes (task 085; no secret, so a fork's pull request runs it too). `.github/workflows/commit-rules.yml` runs `commit-rules`
(`scripts/check-commit.sh` over every pushed commit and the pull request text) on the same pushes and pull
requests. `.github/workflows/advisories.yml` restores and audits the lock files every Monday and by hand (`gh
workflow run advisories.yml`), so an advisory for a package already in a lock file shows up between pull
requests; it is not a required check. `.github/workflows/sync-issues.yml` mirrors the task files into GitHub
issues on every push to `main` that touches `tasks/` or the script (task 069, see [The issue mirror](#the-issue-mirror));
it is not a required check either. The workflows run with a read-only token by default; only the release job
may write contents and only the mirror job may write issues. Every restore runs in locked mode (`RestoreLockedMode` under `CI`), so a lock file out of
step with its project fails instead of being rewritten.

Some of the setup lives in the repository settings, not in a file (task 012, applied 2026-10-29): a ruleset
protects `main` (changes arrive through a pull request with squash merge only, the checks `dotnet`, `deps`,
`frontend`, `sidecar`, `lint`, `workflow-lint`, `windows` and `commit-rules` must pass, the history stays
linear, and force pushes and deletion are blocked; the repository admin may bypass the ruleset for a small
housekeeping commit, see `docs/git-workflow.md`; a job skipped by its `if:` still
satisfies its required check; the checks are not "strict", so a branch need not be rebased on the head of
`main` before the merge); another ruleset keeps `v*` tags from being moved or
deleted; squash merge is the only merge method, with the pull request title and description as the commit
message, and the branch is deleted on merge; Dependabot alerts and security updates are on; private
vulnerability reporting (the link in SECURITY.md), secret scanning and its push protection are on (task 019),
behind the fixture check in CI and the pre-commit checks. The repository is public since task 067 (D-010): its
history was rewritten once, in a fresh clone, before the publication, and pushed to a new repository, so a pull
request number up to 27 in a commit subject refers to the private repository the history was carried from, kept
as the maintainer's archive. D-005 kept the `windows` job off the pushes to `main` to spare a private repository's
billed minutes; a public repository's standard runners are free, so since task 068 (decided with the round of
2026-10-30) the job runs on the squash commit on `main` too, and the published exes and the window's smoke test are
checked on what was merged, not only on the pull request before it.

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
front end's packages from the lock file by `scripts/npm-licenses.py`), signs the product's files when it is given a
certificate (below), copies the usage guide `docs/usage.md` as `GUIDE.md` (task 086: the guide is the support
team's, who have the zip and no repo; the README is a contributor's), and packs everything into
`dist\m365-aegis-<version>.zip`: the two exes, their assemblies, `wwwroot\`, `sidecar\aegis-exo-sidecar.ps1`, the
notices and the guide. No XML documentation file ships, neither ours nor the
packages' (`PublishDocumentationFile` and `PublishReferencesDocumentationFiles` off, task 031; the script fails
when one is left in the folder): they were 919 KB, the WebView2 SDK's 639 KB of it. The publish of 2026-10-30
is 11.6 MB unpacked and 4.7 MB zipped. Next to the zip it writes `SHA256SUMS.txt` (the
zip's SHA-256 in the `sha256sum -c` format) and `release-notes-<version>.md`, whose last line names the signer and
the certificate's thumbprint (or says the exes are not signed). `-DryRun` does all of it in a temporary folder and
prints the sizes, which is what the `windows` job runs on every pull request, signed with a throwaway certificate.

```powershell
dotnet tool restore                      # once per clone
scripts\release.ps1                      # dist\: the zip, SHA256SUMS.txt, release-notes-<version>.md (unsigned)
scripts\release.ps1 -DryRun
scripts\release.ps1 -DryRun -CertificatePath test-signing.pfx -TimestampUrl ''   # as the windows job runs it
```

**The signature** (task 085). With `-CertificatePath <pfx>`, whose password is the `M365AEGIS_SIGNING_PASSWORD`
environment variable (no password when it is unset; never a parameter, never printed), `scripts/sign-release.ps1`
signs the product's own files at the top of the publish folder (`m365-aegis.exe`, `m365-aegis-gui.exe`,
`m365-aegis.dll`, `m365-aegis-gui.dll`, `M365Aegis.Core.dll`: the names `m365-aegis*.exe`, `m365-aegis*.dll` and
`M365Aegis.*.dll`, which the doctor's `signature` row reads too) with `signtool sign /fd SHA256` and, with
`-TimestampUrl` (`http://timestamp.digicert.com` by default), an RFC 3161 timestamp, so a signature stays valid after
its certificate expires; a busy timestamp server is tried three times. The packages' assemblies keep their
publishers' signatures, and the sidecar script stays unsigned: the doctor compares its hash with the copy in the
Core, and a signature block would change it. Then each signed file is verified before the zip is made: its signer
is the given certificate, its hash holds, it carries the timestamp that was asked for, and `signtool verify /pa`
passes, which needs a certificate that chains to a root the machine trusts. A self-signed certificate the machine
does not trust (the CI runner, a contributor's machine) is checked against itself instead: its chain is built with
the certificate as the only trusted root, for code signing. The certificate must hold its private key, the code
signing usage and a date that has not passed. Without `-CertificatePath` the zip is unsigned and the output's
`signing:` line says so; `-RequireSignature`, which the release job passes, fails before anything is built when no
certificate or no timestamp server is given. `signtool` comes from the Windows SDK (on `PATH`, or the newest
`Windows Kits\10\bin\<version>\x64`), which the `windows-latest` runner has. `tests/release/sign-release.Tests.ps1`
(Pester, in the `windows` job) signs copies of the built files with a throwaway certificate that
`tests/release/new-test-certificate.ps1` makes in memory and no store holds, checks that a package's assembly and the
sidecar script are left alone, that a wrong password and a missing file are refused, runs the doctor of a signed
folder (`signature` names the signer and warns that it is not trusted) and of one with a byte changed after the
signing (`signature` fails), and that `release.ps1 -RequireSignature` refuses to start without a certificate.

The certificate lives in the repository's `release` environment, never in the repo: the secret
`SIGNING_CERTIFICATE` holds the PFX file in base64 and `SIGNING_CERTIFICATE_PASSWORD` its password. The release job
writes the file into the runner's temporary folder for the run and removes it afterwards; a tag without the secret
fails the release. Which certificate it is (a self-signed one the team's machines trust, a bought one, or Azure
Trusted Signing) is the decision task 085 drafted; whichever it is, it goes in as a PFX file:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("signing.pfx")) | gh secret set SIGNING_CERTIFICATE --env release
gh secret set SIGNING_CERTIFICATE_PASSWORD --env release     # asks for the value
```

Pushing a tag `v<version>` runs every job; when they pass, the `release` job checks that the tag is on `main`,
runs `release.ps1 -RequireSignature` with the certificate, attests the build provenance of the zip and publishes it
with the checksum file as a GitHub release titled `m365-aegis <version>`, with the notes file as its text, through
the runner's `gh` CLI. A downloaded zip is checked three ways, as `SECURITY.md` and the guide's "Check the download"
tell a reader: its SHA-256 against `SHA256SUMS.txt`, its attestation with `gh attestation verify <zip> --repo
speti4/m365-aegis`, and, unzipped, the signature of each exe (`Get-AuthenticodeSignature .\m365-aegis.exe`, or
`signtool verify /pa` on a machine with the SDK, or the doctor's `signature` row).

### The changelog

`CHANGELOG.md` is generated from the task files by `scripts/changelog.py`: every file under `tasks/done/` is a
line (its H1, linked to the file) under its milestone, in the section of the first release tag whose tree holds
it under `tasks/done/`, or under *Unreleased* when no tag holds it yet. The tag trees decide rather than the
`closed` date, because tasks close both before and after a tag on the same day. The summary under a heading is
written by hand and kept by every run; the lists are never edited by hand. A task that changed the project rather
than the product (the repo, CI, the process) carries `changelog: housekeeping` in its frontmatter and is listed
last, under *Project housekeeping*. The release notes are one section of the changelog without that group (one
line counts it and links the changelog), with a footer on the checksum, the attestation and the notices file. The
script reads the task files through the parser of `scripts/sync-issues.py`, the issue mirror's script. A release
goes like this:

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

### The issue mirror

Every numbered task and decision under `tasks/` is one GitHub issue, mirrored one way by `scripts/sync-issues.py`
(the project-pal task standard's script, template version 3, copied unchanged: a change goes into the template and
comes back through `/tasks mirror`): the id and the H1 as the title, a metadata line and the Context as the body,
labels for the stage, the status and the size, the milestone from the frontmatter (every `## ` heading of
`tasks/roadmap.md` is a milestone; a release closes one, the script never does), open or closed by the folder. The
file is the only truth: an issue is never edited by hand and nothing flows back. `.github/workflows/sync-issues.yml`
runs the script on every push to `main` that touches `tasks/` or the script, and by hand (`gh workflow run
sync-issues.yml`); an issue is found again by a hidden marker in its body, so a run patches only what differs, and
a second run reports `created 0, updated 0`. By hand, from a clone with `gh` signed in:

```powershell
python -I scripts/sync-issues.py --dry-run     # print what would change
python -I scripts/sync-issues.py               # apply
python -I scripts/sync-issues.py --check       # exit 1 when anything would change
```

The backfill ran once on 2026-10-30 (task 069). Since the repository is public, a task file's H1 and Context are
public text: decisions and results, no conversation, `plan: none`, no home folder path.

### The other scripts

- `scripts/check-fixtures.py` — every file under `tests/fixtures`, `tests/sidecar` and `tests/contracts`, the
  accepted snapshots and every Markdown file at the root and under `docs/`, `tasks/` and `.github/` (task 094; the
  usage guide since task 086) carry made-up data only: the made-up domains or a host on its allow-list, each with
  its reason (a domain is any name of two or more labels), the `00000000-…` ids, nothing that looks like a token;
  and no fixture path is over 190 characters from the repo root, which a worktree would push past git for
  Windows' 260. `--self-test` runs the rules against known cases ([the fixtures' rules](../tests/fixtures/README.md#the-rules)).
- `scripts/sign-release.ps1` — the release's signature, called by `release.ps1` ([The release](#the-release)).
- `scripts/check-vulnerable.py` — `dotnet package list --vulnerable --include-transitive` as JSON, with a failing
  exit code when it finds anything (the command itself exits 0 either way).
- `scripts/npm-licenses.py` — the front end's shipped packages against `licenses/allowed.json`, from the lock
  file; `--notices` appends them to the notices file.
- `scripts/check-commit.sh` — the commit rules of [git-workflow.md](git-workflow.md), with a `--self-test`.
- `scripts/pester-version.txt` — the Pester version the `sidecar` job and the `windows` job's signing tests install.

**The usage guide** (`docs/usage.md`, task 086) is the support team's manual and ships in the zip as `GUIDE.md`.
`UsageGuideTests` in the Cli tests hold it to the product: every command of the help tree (the hidden `probe` aside)
is named in it as `m365-aegis <path>`, every card of the window (a `CardTitle` or a `WriteCard` title under
`gui/web/src/components`) has its `### <title>` section, and every example line of its `powershell` blocks runs as a
child process over the recorded answers, the fake Exchange Online module and a home with the sample plan records,
and answers 0 (or 7 for a write without a flag); `--yes` runs as `--dry-run` there, a `--from-csv` file is the sample
of `tests/fixtures/csv` with that name, and the examples that need the real sign-in or the network (`login`,
`logout`, `whoami`, `profile remove`, `doctor` without `--offline`) are only named. A new command or card fails the
tests until the guide has it.

## Coverage

Every test layer is measured, and CI holds each one at 100% (task 070): a change that adds a line or a branch
without a test that runs it fails its job. Each job uploads its report as a run artifact, also when the gate
failed, since that is when it is read.

- **The Core and the command line** (the `dotnet` job, artifact `coverage-dotnet`): `coverlet.MTP` instruments
  `M365Aegis.Core.dll` and `m365-aegis.dll` next to each test project under `dotnet test --coverlet`, and each
  project writes a JSON and a Cobertura report. `scripts/check-coverage.py` merges the JSON reports, so a line
  or a branch counts as covered when either project ran it. It lists every file with an uncovered line or branch,
  writes a table to the job summary, and fails when an assembly is below 100% of its lines or its branches. The Cli
  tests stay child processes, the way a shell runs the exe. The child loads the instrumented `m365-aegis.dll` next
  to the tests and inherits coverlet's environment, so its hits are written to the same report when it exits. The
  exit code, the stdout and stderr split and the pipe-or-terminal default are tested in the same run that measures
  them; no in-process entry is needed (decided 2026-10-30). The safety tests' child processes (the write guard, the
  exit codes, the CSV files, token hygiene) count the same way. The table views are also tested in process
  (`TableViewTests`).
- **The front end** (the `frontend` job, artifact `coverage-frontend`): Vitest with the V8 provider
  (`npm run test:coverage`). `vite.config.ts` sets 100% thresholds for lines, branches, functions and statements
  over `src/**/*.{ts,tsx}`, the generated shadcn/ui components included. Only the test files and their helpers
  under `src/test/` are left out. The component tests run in `happy-dom`.
- **The sidecar script** (the `sidecar` job, artifact `coverage-sidecar`): `aegis-exo-sidecar.ps1` is still one
  file, which the Core embeds and the doctor hash-checks. It takes `-In`, `-Out` and `-Log` (a reader and two
  writers, which default to stdin, stdout and stderr), so a test can drive the whole protocol in its own process
  with string streams. `-Library` defines the functions and returns before the loop, so a test can dot-source the
  script and call one function. The Pester tests run every case twice: as a child pwsh (the process contract) and
  in process (what Pester's profiler-based coverage sees). `tests/sidecar/run-with-coverage.ps1` writes
  `TestResults/sidecar-coverage.xml` (JaCoCo) and fails below 100% of the script's commands, naming each missed one.
  Pester measures commands, not branches. Its own `CoveragePercentTarget` does not fail a run in 5.7, which is why
  the script checks the number.
- **The window's exe** (`src/M365Aegis.Gui`) has no test project and is not measured. What it does with a
  message is the Core's `Bridge` (measured above); the WinForms form, the WebView2 control and the P/Invoke shells
  around them need a desktop session, and the `windows` job's `--smoke` run checks them.

CI measures on Linux. The `windows` job runs the same tests without coverage (no threshold there), the Windows-only
ones included (the home folder's access list, which the `dotnet` job skips): the code that only one operating system
runs is excluded (below), so the same 100% holds on a Windows machine, where a
contributor runs the gate before a commit (CONTRIBUTING.md, "Checks before a commit").

A gap is closed with a test first. A branch that no input can reach is simplified away, without changing the
behavior of any input that can. A dependency a test cannot set up gets a seam (`DoctorEnvironment`, the
transports, `LogSink.UseVerbose`, the `-In`/`-Out` streams of the sidecar). What still needs something CI cannot
give carries `[ExcludeFromCodeCoverage(Justification = "…")]` on the smallest member that needs it. Every one is
listed here, and a new one is added to this list in the change that adds it:

| Member | Why it is not measured |
| --- | --- |
| `MsalTokenBroker`: `AcquireSilentWithOperatingSystemAccountAsync`, `GetAccountsAsync`, `AcquireSilentAsync`, `AcquireInteractiveAsync`, `AcquireWithDeviceCodeAsync`, `RemoveAsync`, `FindAsync`, `GuardAsync`, `BuildAsync` (Core, `Auth`) | Each one builds the real MSAL public client (the Windows account manager, the DPAPI cache file) and calls the real sign-in service. What the broker does around the library is tested: the exception mapping, the result, the window thread, the log; the sign-in flows run over a fake `ITokenBroker`. |
| `MsalTokenBroker.CreateCacheAsync` (Core, `Auth`) | MSAL's cache helper, one side per operating system (DPAPI on Windows, the plain file elsewhere); the token-hygiene tests run it on Windows. |
| `AegisHome.CreateNew` and `AegisHome.UserOnly` (Core, `Config`) | The creation of a missing home: the Windows access list, a plain folder elsewhere, one side per operating system. The home folder's access list tests check the Windows side in the `windows` job (they skip on Linux, see [The safety tests](#the-safety-tests)); what calls them (`AegisHome.Create`, `CreateAbove`) is measured. |
| `ConsoleWindow.RootOwner` (Core, `Auth`) | Win32 calls on the console window of a desktop session, which CI's runners do not have. |
| `BridgeServices.ForWindow` (Core, `Bridge`) | Wires the window's real layers: MSAL over the window, Graph, and an Exchange Online session that a first cmdlet would start against the tenant. The `windows` job's `--smoke` run starts the window with it; each layer it wires is measured on its own. |
| `Doctor.PwshFileName` (Core, `Diagnostics`) | `pwsh.exe` or `pwsh`: one side per operating system. The search over `PATH` is tested with a made-up file name. |
| `DoctorEnvironment`: `ReadHomeAccessFromDisk`, `RunDsregcmd`, `ReadDesktopRuntimeVersionsFromDisk`, `ReadWebView2VersionsFromRegistry`, `ReadPv`, the nested `Network` (the endpoint probe and its `HttpClient`) (Core, `Diagnostics`) | They read the real machine (the home folder's access list, `dsregcmd`, the registry, the .NET installation's folders, Microsoft's endpoints), differently on Windows and on Linux. The checks over their answers are tested through the `DoctorEnvironment` seams; the access list reader also runs in the home folder's access list tests of the `windows` job. |
| `Authenticode`: `ReadProductFiles`, `Verify`, `Signer` (Core, `Diagnostics`) | `WinVerifyTrust` and its state over the files next to the running assembly, Windows only. The names of the product's files and the verdict of each `WinVerifyTrust` code are tested, the check over the answers through the `DoctorEnvironment.ReadSignatures` seam; the interop itself runs in the `windows` job's signing tests, over a signed folder and a changed one. |
| `SidecarClient.KillQuietly` (Core, `Exchange/Sidecar`) | Its catch runs only when the operating system refuses the kill, or the child ends between the check and the call: a race no test can stage. |
| `SidecarClient.WriteQuietlyAsync` (Core, `Exchange/Sidecar`) | Its catch runs only when a message reaches a child that has ended and nothing else holds the pipe's read end at that moment; whether the write fails or not, the read after it meets the end of the stream and says the sidecar stopped, which is tested. |
| `SidecarClient.AwaitQuietly` (Core, `Exchange/Sidecar`) | Its catch runs only when the pipe breaks under the read, which .NET reports as the end of the stream on every runner. |
| `DeviceCodePrompts.OnStderr` (Cli) | The console's stderr during a device-code sign-in, which needs the real sign-in service; the text it writes is tested through `DeviceCodePrompts.Write`. |

```powershell
# The Core and the command line (delete the folder first, so no older report is merged in)
Remove-Item -Recurse -Force TestResults/coverage -ErrorAction Ignore
dotnet test --no-build -c Release --coverlet --coverlet-output-format json --results-directory TestResults/coverage
python -I scripts/check-coverage.py TestResults/coverage
# The front end; the HTML report is gui/web/coverage/lcov-report/index.html
npm run test:coverage --prefix gui/web
# The sidecar script
pwsh -File tests/sidecar/run-with-coverage.ps1
```

## Fixtures

Every test runs offline and against synthetic data: recorded Graph, Admin API and Exchange Online answers
scrubbed to made-up names under `tests/fixtures/` (`fixtures/README.md` has the layout and the rules), the
bridge contract samples under `tests/contracts/bridge/`, and the fake `ExchangeOnlineManagement` module under
`tests/sidecar/modules/`. A signed-in round on the real tenant is a task of its own, and its result is a dated
line in that task's Verification; nothing it printed is committed before it is scrubbed. A Graph fixture's file
name comes from the hidden `m365-aegis call GET "<path>" --fixture-name`, which computes it as the fixture
transport does, sends nothing and needs no sign-in (task 036).

The Exchange Online Admin API answers from the same folder (task 042): its host, `outlook.office365.com/adminapi/…`,
mirrored as a path beside Graph's, for the made-up tenant `00000000-0000-0000-0000-0000000000a0` and the account
`alice@contoso.com`, so `M365AEGIS_GRAPH_FIXTURES` makes the command line read both. Every Admin API call is a POST
to the endpoint's one URL, so the cmdlet's parameters name its fixture too, sorted with the query after the `@`
(`Mailbox@Identity=finance&IncludeGrantSendOnBehalfToWithDisplayNames=true.json`). A Graph `$batch` call without a
recorded answer of its own is answered item by item from each item's own fixture (task 041), and an answer that is
not JSON, the usage report's CSV, is a `.csv` file (task 043). With `M365AEGIS_EXO_TEST_ORGANIZATION` alone the
command line has no Admin API and the mailbox lookups read the fake module only.

The writes' answers (task 052) are recorded the same way: a `$ref` POST is named by its URL and the id its body
points at (`groups/<id>/members/$ref@@odata.id=<object id>.headers`, with `Status: 204` or the refusal), a DELETE by
its URL alone; the sample CSV files of `--from-csv` are under `tests/fixtures/csv/` (task 054). The fake module
records each Exchange write it is sent in the file `M365AEGIS_FAKE_EXO_WRITES` names (task 053).
