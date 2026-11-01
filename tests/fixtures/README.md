# Fixtures

Recorded answers for the offline tests, scrubbed to made-up data: a real tenant name, user, mailbox, object id or
token never enters the repo (CONTRIBUTING.md, "Tenant data never enters the repo"). Every test runs offline and
against what is here; a manual round on the real tenant is a task of its own, and its result is a dated line in
that task's Verification. `scripts/check-fixtures.py` holds every file here, and the accepted snapshots under the
test projects' `Snapshots/` folders, to the rules below and runs in CI.

## The rules

- Domains are `contoso.com`, `fabrikam.com`, `example.com`, their subdomains (`mail01.contoso.com`), their
  `.onmicrosoft.com` forms or their SharePoint hosts (`contoso.sharepoint.com`, `contoso-my.sharepoint.com`); any
  other host is a finding unless it is on the scan's allow-list.
- GUIDs are of the form `00000000-0000-0000-0000-0000000000NN` (the last two digits tell the objects apart).
- No token or anything that looks like one (a `eyJ…` prefix fails the check); an `Authorization` header in a
  recorded request is replaced by `Bearer <redacted>`.
- A recording is scrubbed by hand and then run through the anonymizer scan before it is committed.

### What the scan reads and how it tells a domain (task 094)

- **Where.** Every text file here, under `tests/sidecar` and `tests/contracts` and in the test projects'
  `Snapshots/` folders, and the repo's prose: every Markdown file at the root and under `docs/` (the usage guide
  included), `tasks/` (the GitHub issues mirror them) and `.github/`; a Markdown file git ignores (the maintainer's
  `CLAUDE.local.md`) is skipped, since it never enters a commit. The domain, GUID and token rules hold in all of
  them; the path length below holds for the fixture folders.
- **A domain** is any name of two or more labels that ends in one of the scan's `TOP_LEVEL_DOMAINS` (`com`, `net`,
  `org`, `hu`, `eu`, `de`, `io` and a few more): `alice@<company>.hu` is a finding, as is
  `<company>.sharepoint.com`. A URL escape is not part of a host (`alice%40contoso.com` in a file name is
  `contoso.com`).
- **The allow-list.** `ALLOWED_HOSTS` in `scripts/check-fixtures.py` names every other host the repo may carry,
  each with a one-line reason: Microsoft's endpoints and docs, the package registries, the libraries' homes, the
  research's sources and a few code names that look like hosts (`System.IO`). `*.name` allows the name and its
  subdomains, a bare name only itself; `onmicrosoft.com`, `sharepoint.com` and `outlook.com` stay bare, since a
  tenant's own name sits under each. `ALLOWED_GUIDS` does the same for the few GUIDs that are nobody's tenant
  (Microsoft's message trace application). A new link in a doc adds its host there, with the reason, in the same
  change; a tenant's value is rewritten to the made-up names instead.
- **The self-test.** `python -I scripts/check-fixtures.py --self-test` runs the rules against known lines (a
  two-label domain, a SharePoint or `.onmicrosoft.com` host of another tenant, a real GUID, a JWT are findings;
  the made-up names, the allowed hosts and file names are not) and against a fixture written with a two-label
  address in it; the `lint` job runs it next to the scan.

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

## The owners-missing judgment: distribution groups by ManagedBy

Task 089. The groups the ownerless queries list have ids of their own now, apart from the groups of the writes
bundles: All Staff (`…03b`, a distribution group), HR Synced (`…03c`, a synced security group) and Sales Managers
(`…03d`, a mail-enabled security group), in the wide query's two pages and the security groups' narrowed query. The
fake module's `Get-DistributionGroup` knows All Staff with an empty `ManagedBy` and Sales Managers managed by alice
(`tests/sidecar/README.md`), so `group owners-missing --all-types` lists the one and leaves out the other.

## The safety tests bundle: the write guard's samples

Tasks 071 to 075. `csv/mailbox-forwarding-clear.csv` (bob, who forwards to `bob@fabrikam.com`, and carol, who forwards
to the recipient Finance) is the `--from-csv` sample of `mailbox forwarding clear`, which the write-guard test runs
next to the other writes' samples (task 071).

`csv/edge-cases/<family>/<case>.csv` are the CSV edge cases of task 074: for each set of columns (`group`:
`group,user`; `mailbox-access`: `mailbox,user,right`; `forwarding-set`: `mailbox,to,keepCopy`; `forwarding-clear`:
`mailbox`) one file per case, `bom-crlf` (a UTF-8 byte order mark and CRLF, as Excel writes "CSV UTF-8"), `lf`,
`quoted-comma` (a made-up name with a comma in quotes: `"Finance, EU"`), `surrounding-spaces`, `header-case-order`,
`duplicate-line` (line 3 repeats line 2), `trailing-empty-lines` and `malformed-last-line` (line 4). Each carries the
same two lines (Finance with dave and bob; finance and reception; alice and carol forwarding to `fabrikam.com`; bob and
carol) so the dry runs plan against the recorded answers and the fake module. The rules of a CSV fixture: the made-up
names of every fixture, no file without a case in `CsvEdgeCaseTests.CaseNames` (the test checks every family has
exactly those), and the bytes as written, which `.gitattributes` keeps (`tests/fixtures/** -text`): an editor that
saves the BOM away or the line endings to LF breaks the case, so a change is made with a script that writes bytes.

## The snapshot fixture

Tasks 087 and 084. The fixture snapshot is never committed: `tests/shared/SnapshotSamples.cs` takes it once per test
run, in process, over the recorded answers here and the fake module (the take's clock at 2026-10-31T06:00:00Z, so its
name is `2026-10-31T060000Z.sqlite`, about 128 KB), and copies it into each test's temporary home. The answers the take
reads beyond the lookups' own:

- `v1.0/users@$select=id,displayName,userPrincipalName,mail&$top=999`: alice, bob, carol (without an address), dave,
  eve, and the accounts of the shared mailboxes Finance (`…015`) and Reception (`…016`), whose ids the fake module's
  mailboxes carry too. The fake module gives the mailboxes of alice to dave the ids `…011` to `…014`, which Graph does not; the take
  ties each mailbox to its user by the user principal name.
- `v1.0/groups@$select=…&$top=999`: Finance (`…031`, Microsoft 365, a team), Finance EU (`…032`, security), Project
  Phoenix (`…033`, Microsoft 365, a team), Finance Staff (`…035`, mail-enabled security), Sales (`…036`, distribution)
  and Sales Synced (`…03a`, distribution, synced); the members of Project Phoenix (dave, eve), Sales (bob, alice and
  the contact Dave Private, `…030`) and Sales Synced (eve), and the owner of Finance EU (carol). Finance's, Finance
  EU's and Finance Staff's members and the other owners are the lookups' fixtures; Finance Staff's and Sales' owners
  come from the fake module's `ManagedBy`.
- `v1.0/sites/<site id>/drive@$select=id,owner`: Finance's library (`drive-finance`) and Project Phoenix's
  (`drive-phoenix`), each owned by its group, and a 403 for the communication site.
- `v1.0/drives/drive-finance/root/delta@…` over two pages (the root, `Budget.xlsx` and `Reports` marked shared,
  `Plan.docx` not) and `drive-phoenix`'s in one (`Plans/Roadmap.pptx` marked), each ending in a made-up delta link.
- `v1.0/drives/<drive>/items/<item>/permissions`: `Budget.xlsx` over two pages (the site's Owners group, an
  organization view link, a users edit link for alice, the external `partner@fabrikam.com` and a SharePoint identity
  alone, a grant to bob, and a grant inherited from the root), `Reports` (a grant to Finance EU, an anonymous view link
  with a password and an expiry) and `Roadmap.pptx` (a users view link for Finance Staff, a grant to dave).
- On the Admin API's side, `MailboxFolderPermission@Identity=<address>%3A%5C<calendar>&ResultSize=Unlimited` for the six
  mailboxes by address: `Default` free/busy everywhere but bob's (Reviewer), `Anonymous` free/busy on dave's, Finance
  Staff a Reviewer on carol's, alice an Editor on Finance's and bob a Reviewer on Reception's `Kalender`.

So dave reaches Finance and Finance Staff through Finance EU, the sites of Finance and Project Phoenix, `Reports`
through Finance EU, `Roadmap.pptx` directly and through Finance Staff, Reception's FullAccess and carol's calendar
through Finance Staff: the answer `SnapshotAccessTests` holds.

Task 098's changed snapshot (`SnapshotSamples.SeedChangedAsync`) is the same file a day later,
`2026-11-01T060000Z.sqlite`, changed with SQL in the test's home: the device left Finance EU and bob joined Project
Phoenix, carol no longer owns Finance EU and dave owns Project Phoenix, the organization link on `Budget.xlsx` is gone
and bob holds the users link on `Roadmap.pptx`, Finance Staff lost Reception's FullAccess and dave got Finance's,
carol's `Default` calendar entry went from free/busy to Reviewer, bob's external forwarding is gone and alice forwards
to carol, keeping a copy. The group Finance is renamed Finance Team and every integer key is moved by 100, which
`snapshot diff` must not show as changes. Task 114 adds a new group, Audit (`…-000000000037`): bob is its member and
Audit is a member of Finance Staff, so bob gains Finance Staff through Audit, and with it the users link on
`Roadmap.pptx` and carol's calendar; `userGroups` follows the new memberships as a take computes it, so `snapshot
diff --user bob@contoso.com` lists those rows with the group they come through.
