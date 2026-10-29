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
- `adminapi/<path>.json` — an Exchange Online Admin API answer in the same shape, once an Admin API backend exists
  (task 010 probed the API; its test answers with a scripted handler).
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
