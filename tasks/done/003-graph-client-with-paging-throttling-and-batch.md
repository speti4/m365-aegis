---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 003 — Graph client: HttpClient and System.Text.Json, paging, throttling, `$batch`, v1.0 and beta; the raw `call`

## Context

`Graph` in the core over `HttpClient` and `System.Text.Json`: an `IGraphTransport` seam the tests replace with
recorded fixtures (`tests/fixtures/graph`, the URL mirrored as the path); `@odata.nextLink` paging as an async
stream; 429 and 503 with `Retry-After` honored with a jittered back-off; `$batch`; `ConsistencyLevel: eventual`
where a query needs it; a v1.0/beta switch per call; typed records with unknown fields preserved; the request
ids collected into the envelope's `meta`. The raw `call` command (`call GET /v1.0/me/memberOf --beta`) prints an
answer unchanged inside the envelope. The token comes from `AuthService.GetAccessTokenAsync` for the Graph
resource (task 002).
Why: every lookup of M1 and the Graph-side Exchange backend (D-004) go through this; the direct client is the
alternative to a generated SDK (D-002).
Done when: the paging, throttling and batch behavior is covered by fixture tests that run offline, `call` reaches
a live endpoint after a `login` (the live check runs in the main session's signed-in round), and no fixture
carries a real tenant's data (`check-fixtures.py`).
Bundled with 005 and 007 on one branch (one pull request for the three, main session's decision of 2026-10-29);
task 006 is built on this client from a second branch, so the client's public surface is pushed first and then
kept stable. The task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`: a "The Graph client" section (the transport seam, the fixture layout, paging, throttling,
  batch, the beta switch, the request ids in `meta`) and the `call` command
- `tests/fixtures/README.md`: the `graph/` layout (the URL mirrored as the path)
- `README.md`: `call` in the first-run section

## Not in this change

- the lookups themselves (005, 007 on this branch; 006 on its own branch)
- the Exchange Online Admin API backend (task 010) and the sidecar (task 015)
- a generated SDK or Kiota client (D-002)
- a request log on disk (the request ids go into the envelope's `meta` only)

## Steps

1. [x] `src/M365Aegis.Core/Graph/`: `IGraphTransport`, the `HttpClient` transport with the bearer token, the
       fixture transport (in the Core, so the command line can run over the fixtures too)
2. [x] paging (`@odata.nextLink`) as `IAsyncEnumerable`, the `Retry-After` back-off with jitter and a retry
       budget that ends in `ExitCode.Throttled`, `$batch`, `ConsistencyLevel: eventual`, the v1.0/beta switch
3. [x] typed records with unknown fields preserved (`JsonExtensionData`), the request ids into `EnvelopeMeta`
4. [x] `Commands/CallCommand.cs`: `call <METHOD> <path> [--beta] [--body] [--all] [--yes]` inside the envelope
5. [x] fixtures under `tests/fixtures/graph` (contoso.com, `00000000-…-00NN` ids), the tests, `check-fixtures.py`
6. [x] docs; the branch pushed (no pull request yet) and the main session told, so task 006 can branch off
7. [x] the live check in the main session's signed-in round (`call GET /me`, `call GET /me --beta`)

## Log

- 2026-10-29 — promoted by the main session into the Graph bundle (003, 005, 007)
- 2026-10-29 — verified against the Graph docs before coding: JSON batching (POST `/{version}/$batch`, at most 20
  requests, `id`/`method`/`url` relative to the version, `headers` (with `ConsistencyLevel` inside a batch,
  `Content-Type` when there is a body), answers in any order with their own `status`/`headers`/`body`, a
  throttled item is a 429 inside a 200 and is retried in a new batch after the longest `retry-after`;
  https://learn.microsoft.com/graph/json-batching), throttling (429 with `Retry-After` in seconds; without it an
  exponential back-off; https://learn.microsoft.com/graph/throttling), advanced queries (`ConsistencyLevel:
  eventual` plus `$count=true`, except `$search`, which needs only the header; `$expand` is not supported with
  them; https://learn.microsoft.com/graph/aad-advanced-queries), paging (`@odata.nextLink` is an absolute URL used
  as it is; https://learn.microsoft.com/graph/paging)
- 2026-10-29 — built: `Graph/` in the Core (`GraphRequest`/`GraphQuery`, `IGraphTransport` with
  `HttpGraphTransport` and `FixtureGraphTransport`, `GraphClient` with `SendAsync`, `GetAsync<T>`,
  `GetPagesAsync<T>`/`GetAllAsync<T>`/`ListAsync<T>`, `BatchAsync`, `GraphObject` with the extension data,
  `GraphConnection`), `RequestLog` in `Output` and a `CommandRunner.RunAsync` overload that hands it to the work,
  `GraphRunner` in the Cli (one call per Graph command), `call` with `CallTableView`. Decisions taken on the way:
  `call` sends any method, but a non-GET only with `--yes` (else the request is the data and the exit code 7,
  D-006), rather than being GET-only, so a one-off fix stays inside the contract; a version prefix in the path
  wins, and one that disagrees with `--beta` is a usage error; the throttling budget is 4 retries per request and
  60 s of waiting per command, a `Retry-After` past it fails at once (a terminal should not hang for minutes);
  two failure kinds are new, as D-006 prescribes for a condition no code covers: `forbidden` (exit 8, a 403:
  the hint names the permission) and `service-error` (exit 9, a 400, a 5xx after the retries, no network);
  `ITableView` gained `HeadersFor(data)` (default: `Headers`) for the raw answer's columns. The fixture names map
  a query string after `@`, decoded, sorted, with `%XX` for what a file name cannot hold; the tests read them from
  the source tree (MSBuild would unescape the `%XX` of a copied item), and `M365AEGIS_GRAPH_FIXTURES` makes the
  exe itself answer from them, so the child-process tests run `call` offline. 136 tests green; the fixtures run
  through the anonymizer scan (it flags the contoso addresses and the zero GUIDs, both made up)
## Deviations from the plan

- `call` sends any method rather than GET only; a non-GET needs `--yes` and is otherwise the plan with exit code
  7 (D-006).
- Two exit codes are new, 8 `forbidden` and 9 `service-error`, for the Graph answers no code covered (D-006: a new
  condition gets a new number).
- The fixture transport lives in the Core, not in the tests, so `M365AEGIS_GRAPH_FIXTURES` can point the exe at
  the fixtures; the fixtures are read from the source tree, not copied to the build output.
- `ITableView` gained `HeadersFor(data)` for the raw answer's columns.

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the Release build with and without the front end (0 warnings), `dotnet test` 173 tests green, `changelog.py --check`, `check-fixtures.py` (92 files, 0 findings); with an empty `M365AEGIS_HOME` the exe printed its version, `doctor --offline` exited 0, `whoami` exited 3; `release.ps1 -DryRun` green. The new fixtures were run through the anonymizer scan, which flagged only the made-up contoso addresses and the zero GUIDs
- 2026-10-29 — against the tenant, in the main session's signed-in round on the maintainer's machine over Remote
  Desktop, from a shell with stdout redirected and `M365AEGIS_GRAPH_FIXTURES` unset: `--json call GET /me` exit 0,
  the `/me` object unchanged, one request id in `meta.requestIds`, 1.6 s cold; `call GET /me --beta` exit 0, 79
  properties, one request id; `call GET /v1.0/me --beta` exit 2 (the path and the switch disagree); `call POST
  /groups --body '{}'` exit 7, the plan (method, URL, body) as the data, nothing sent (no request id). Before the
  permissions were consented, `group owners-missing` exited 8 `forbidden` with the 403 sentence and the hint naming
  `GroupMember.Read.All`

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session proposes them to the
      maintainer and files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
