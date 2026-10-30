# Fixtures

Recorded answers for the offline tests, scrubbed to made-up data: a real tenant name, user, mailbox, object id or
token never enters the repo (CONTRIBUTING.md, "Tenant data never enters the repo"). Every test runs offline and
against what is here; a manual round on the real tenant is a task of its own, and its result is a dated line in
that task's Verification. `scripts/check-fixtures.py` holds every file here, and the accepted snapshots under the
test projects' `Snapshots/` folders, to the rules below and runs in CI.

## The rules

- Domains are `contoso.com`, `fabrikam.com`, `example.com` or their `.onmicrosoft.com` forms; nothing else.
- GUIDs are of the form `00000000-0000-0000-0000-0000000000NN` (the last two digits tell the objects apart).
- No token or anything that looks like one (a `eyJ…` prefix fails the check); an `Authorization` header in a
  recorded request is replaced by `Bearer <redacted>`.
- A recording is scrubbed by hand and then run through the anonymizer scan before it is committed.

## Layout

The tasks that add the fixtures name the files; the layout mirrors the request:

- `graph/<host>/<path>.json` — a Graph answer, the URL mirrored as the path, written by hand from the
  documented response shape (task 003): `GET https://graph.microsoft.com/v1.0/me` is
  `graph/graph.microsoft.com/v1.0/me.json`. A query string follows an `@`, its parameters decoded and sorted by
  name and joined with `&` (`v1.0/users@$select=id,displayName&$top=2.json`); a character a file name cannot hold
  (`\ / : * ? " < > |`, and `%` itself) is written as `%XX` (`$search="displayName:Alice"` is
  `$search=%22displayName%3AAlice%22`). The next page is the URL of the first page's `@odata.nextLink`, mapped the
  same way (a made-up `$skiptoken=page-2`). A `<name>.headers` file next to the answer holds response headers, one
  `Name: value` per line, and a `Status: 429` line for a status other than 200 (an error answer may be headers
  alone). The n-th time the same URL is asked, `<name>.try-n.json` / `.try-n.headers` answer when they exist, which
  is how a throttled request and its retry are recorded (`v1.0/organization.try-1.headers` with `Status: 429` and
  `Retry-After: 2`, then `v1.0/organization.json`); a `$batch` call is `v1.0/$batch.try-n.json`. A request id is a
  made-up GUID (`request-id: 00000000-0000-0000-0000-0000000000f1`). `FixtureGraphTransport.RelativePath` computes
  the name, and a missing fixture fails with the name it looked for. A file name longer than 120 characters, or a
  path under `graph/` longer than 150, is stored in its short form (`FixtureGraphTransport.ShortRelativePath`: the
  first 60 characters of the query, a `~` and 8 hex digits of its SHA-256), which the transport tries second and a
  missing fixture's message names; git for Windows cannot add a path over 260 characters, and a worktree under
  `.claude/worktrees` puts about 70 in front of the repo's own (the group queries' names and the message trace's
  windows are stored this way). `scripts/check-fixtures.py` refuses any file here whose path from the repo root is
  over 190 characters. To find a new fixture's name without running a test, ask the command line, which sends
  nothing and needs no sign-in: `m365-aegis call GET "<path or URL>" --fixture-name` (a hidden option; `--beta`
  for the beta endpoint) prints the full name, the stored name, the file to write from the repo root and its
  length. The answers so far: the client's own cases (`me`, `users` over two pages,
  a throttled `organization`, a 503 `domains`, a `$batch` with a throttled item, 403, 404 and 400 answers; task
  003), the group lookups (`groups@…` for the exact name, the address and the search, the members over two pages,
  the owners, the groups without an owner over two pages), `teams` and `sites` (task 005), the narrowed ownerless
  queries (the Microsoft 365 groups, the security groups without mail, and `groups/$count@…` answering 5; task
  032), Finance's `transitiveMembers` (the direct members plus Dave Example and the device FIN-KIOSK-01 through
  Finance EU, in the short form; task 033), and the message traces
  under `v1.0/admin/exchange/tracing/` with a window ending 2026-10-20T12:00:00Z (task 007), with the details of
  their messages (task 034): the rows by trace id (`messageTraces@$filter=id eq …`; an unknown id answers an
  empty list) and by Message-ID, and `messageTraces/<trace id>/getDetailsByRecipient(recipientAddress='…')` for
  bob and carol (received, submitted, delivered) and dave (a failed lookup of the recipient); alice's window
  filtered by the service on `status eq 'failed'` and `contains(subject,'Quarterly')`, and a made-up 400 for
  `contains(subject,'invoice')` that the lookup answers client-side (task 035). An `@` inside a path
  is written `%40`, so a name's first `@` is the query's. The tests read these files from the source
  tree (`tests/shared/GraphFixtures.cs`), and the command line reads them when `M365AEGIS_GRAPH_FIXTURES` names
  the folder.
  The audit lookups (task 006) answer from `v1.0/auditLogs/signIns@…` (the window from 2026-10-28T12:00:00Z:
  alice's four sign-ins over two pages, two of them failed; bob's 403 of a tenant without Microsoft Entra ID P1;
  carol's 403 of a missing permission), `v1.0/applications@…` (two pages: two secrets that expire in the 30 days
  after 2026-10-29T12:00:00Z, one expired, a certificate beyond the window) and `v1.0/servicePrincipals@…` (a
  SAML signing certificate in the window), and `v1.0/auditLogs/directoryAudits@…` (three entries, and the one an
  `--activity "Add member"` filter keeps; it and alice's second page in the short form), the two entries alice
  started (`--user`, task 037), and a window ending 2026-10-29T00:00:00Z over two pages of two entries (`--until`,
  the truncated footer of task 038). The beta sign-in log answers one kind each under `beta/auditLogs/signIns@…`
  from the same window (task 039): one interactive and two non-interactive sign-ins of alice (one failed by a
  sign-in frequency check), two of the service principal Payroll Connector (one with an expired secret), and one of
  a managed identity. The IP addresses are from the documentation ranges (`203.0.113.0/24`, `198.51.100.0/24`).
  The Exchange Online connect reads the tenant's initial domain from
  `v1.0/organization@$select=verifiedDomains.json` (task 015).
- `graph/outlook.office365.com/adminapi/…` — the Exchange Online Admin API's answers, the next section.
- The Exchange Online cmdlets have no recorded answers here: the sidecar tests run the real script against the fake
  `ExchangeOnlineManagement` module, whose made-up mailboxes are listed in `tests/sidecar/README.md` (tasks 015 and
  016).
- `doctor/<state>.json` — a doctor report as the envelope carries it, the samples the snapshot tests of both
  renderings read through `tests/shared/DoctorSamples.cs` (tasks 004 and 008): `healthy.json` (an online run,
  every check passes), `pwsh-missing.json` (an offline run with the required pwsh check failed),
  `warnings.json` (warnings and a failure of a check that is not required, each with its fix).
- `doctor/dsregcmd-<state>.txt` — a made-up `dsregcmd /status` output for the device check (task 008):
  `dsregcmd-entra-joined.txt` (with a primary refresh token), `dsregcmd-registered.txt` (registered only, the
  case task 013 measured), `dsregcmd-neither.txt`; the device name and the tenant in them are made up too.
- `auth/<state>.json` — a sign-in result as the envelope carries it (`signed-in.json`, `no-exchange-token.json`:
  the account, the tenant and object ids, a token's expiry and scopes per resource, never a token), read through
  `tests/shared/AuthSamples.cs` (task 002).

The bridge contract samples the C# and the TypeScript tests share are under `tests/contracts/bridge/`, and the
fake Exchange Online module the sidecar tests run against is under `tests/sidecar/modules/`.

## The Exchange M2 bundle: the Admin API, the group expansion, the usage report

Tasks 041 to 044. The Exchange Online Admin API answers live under
`graph/outlook.office365.com/adminapi/v2.0/00000000-0000-0000-0000-0000000000a0/` (the made-up tenant
`AdminApiConnection.TestTenantId`), named like a Graph fixture with the cmdlet's parameters of the POST body joined
to the query (`FixtureGraphTransport.RelativePath(uri, body)`): `Mailbox@Identity=finance&IncludeGrantSendOnBehalfToWithDisplayNames=true`
(Finance with its delegate Bob Example by name, in the form the service writes, `(bob@contoso.com, Bob Example)`),
`reception` the same without delegates, `alice` refusing the display names parameter (400) and answering without it, `bob` a 403,
`nobody` a 404; `Mailbox@$select=…&ResultSize=Unlimited` (in the short form) the six mailboxes over two pages; `MailboxFolderPermission` for
`finance:\Calendar` (Default, Anonymous, and Alice Example as an object with delegate flags), `reception:\Kalender`
(the localized default calendar the fake module names) and a missing folder
(a 400 that says so); `DistributionGroupMember` for `sales` over two pages (Bob Example, the contact Dave Private,
Alice Example hidden), `nobody` (404) and `busy` (a 429, then an empty list); `OrganizationConfig` and
`AcceptedDomain`. On the Graph side: the members and transitive members of the mail-enabled security group Finance
Staff (`groups/…035`: Carol Example and Finance EU, whose members are Dave Example and FIN-KIOSK-01, read from
`groups/…032/members`), the groups of carol and dave (`users/<upn>/transitiveMemberOf/microsoft.graph.group@…`, in
the short form), and the mailbox usage report `reports/getMailboxUsageDetail(period='D7').csv` (refreshed
2026-10-28: five of the six mailboxes, and a deleted one). The client's `$batch` test answers from its items' own
fixtures (`users/…001`, `users/…099`, `groups/…031` throttled once), with `$batch.try-n.headers` for the batch call's
request ids.

## The Exchange and window bundle: the automapping file

Task 059. `csv/mailbox-grants-automapping.csv` grants carol FullAccess on Reception with `automapping` false, Send As
on Finance with the cell empty, and bob FullAccess on Finance with `yes`.

## The writes and plan records bundle: the batch answers and a distribution group

Tasks 058 to 062. `batches/` holds two recorded `$batch` answers of twenty `$ref` writes, as Graph writes them (the
parts out of order, a `204` part with `Cache-Control` only): `ref-writes-twenty.json` (every part applied) and
`ref-writes-one-failed.json` (part 7 a 403 `Authorization_RequestDenied` with its error body). The Core test's
`RecordedBatchTransport` answers a batch call's `n`-th item from part `n`, so a plan of a hundred member changes is
five calls over them; a batch call without a recorded answer is answered item by item from `graph/` as before (the
group writes' own POST and DELETE answers). On the Graph side, the distribution group Sales (`…036`, by its exact
name, in the short form) and the mail-enabled security group Finance Staff by its id
(`groups/…035@$select=id,displayName,mail,groupTypes,securityEnabled,onPremisesSyncEnabled`); on the Admin API's,
`DistributionGroupMember@Identity=sales@contoso.com&ResultSize=Unlimited` (Bob Example, the contact Dave Private as
`dave@example.com`, as the fake module knows him, and Alice Example hidden) and `…Identity=finance-staff@contoso.com…`
(Carol Example), request ids `…038` and `…039`.

## The window bundle: Finance by its address

Task 063. `Mailbox@Identity=finance@contoso.com&IncludeGrantSendOnBehalfToWithDisplayNames=true` is the Admin API's
answer for Finance asked by its address, as the window's access card sends it after a pick (the bridge sample
`mailbox-access.json`): the same body as `Identity=finance`, request id `…0a1`.

## The M3 writes bundle: the write answers and the CSV files

Tasks 051 to 054. A write's answer is named like a read's, with one rule more: a `$ref` POST, whose URL is the same
for every object added to a list, is named by the id its body's `@odata.id` points at, joined to the query after the
`@` (`FixtureGraphTransport.RelativePath(uri, body)`): `groups/…031/members/$ref@@odata.id=…005.headers` (adding Dave
Example to Finance, `Status: 204`), `…007` (Eve Example, a 403 `Authorization_RequestDenied` with its `.json` body),
and `groups/…031/owners/$ref@@odata.id=…003` (Bob Example as an owner, 204). A DELETE is named by its URL:
`groups/…031/members/…004/$ref` (Carol Example) and `groups/…031/owners/…001/$ref` (Alice Example, Finance's last
owner), both 204. The users a write adds are read through `users/<upn>@$select=id,displayName,userPrincipalName,mail`:
dave (`…005`), bob (`…003`), eve (`…007`, a user only these fixtures know) and nobody (a 404). The request ids of the
writes are `…0ec` to `…0ef` and `…083` to `…088`.

`csv/` holds the sample files of `--from-csv` (task 054), read from the source tree through
`tests/shared/PlanSamples.cs`: `group-members-add.csv` (Finance with dave, bob and eve: one change applied, one
member already, one refused), `mailbox-grants.csv` (three grants against the fake module, the mail contact's
refused, one line in quotes), `mailbox-grants-malformed.csv` (an unclosed quote on line 3) and
`mailbox-forwarding-set.csv` (semicolons, the optional `keepCopy` column). They carry the same made-up names as
every fixture.

## The writes follow-ups bundle: a synced distribution group

Tasks 064 to 066. The distribution group Sales Synced (`groups/…03a@$select=id,displayName,mail,groupTypes,securityEnabled,onPremisesSyncEnabled`,
`sales-synced@contoso.com`, synced from on-premises Active Directory) and its owners on Graph (`groups/…03a/owners@…`,
none), so an owner write on a synced distribution group stays on Graph with its warning. The owners of Sales and
Finance Staff come from the fake module's `ManagedBy` (`tests/sidecar/README.md`); the resume tests of task 065
answer the reads of a hundred made-up users (`…060` to `…0c3`) in code, not from files.
