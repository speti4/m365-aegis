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

- `graph/<host>/<path>.json` — a Graph answer, the URL as the path (`graph/graph.microsoft.com/v1.0/groups.json`);
  a query string is written as `@key=value&key2=value2` in the file name, a page as `.page-N.json`, and a
  `<file>.headers` sidecar adds response headers or a `Status:` line (task 003).
- `adminapi/<path>.json` — an Exchange Online Admin API answer in the same shape (task 010).
- `exo/<cmdlet>.json` — the objects an Exchange Online cmdlet returns through the sidecar (task 015).
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
