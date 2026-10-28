---
status: ready
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Graph client: HttpClient and System.Text.Json, paging, throttling, `$batch`, v1.0 and beta; the raw `call`

What: `Graph` in the core over `HttpClient` and `System.Text.Json`: a `IGraphTransport` seam the tests replace
with recorded fixtures (`tests/fixtures/graph`, the URL mirrored as the path); `@odata.nextLink` paging as an
async stream; 429 and 503 with `Retry-After` honored with a jittered back-off; `$batch`; `ConsistencyLevel:
eventual` where a query needs it; a v1.0/beta switch per call; typed records with unknown fields preserved; the
request ids collected into the envelope's `meta`. The raw `call` command (`call GET /v1.0/me/memberOf --beta`)
prints an answer unchanged inside the envelope.
Why: every lookup of M1 and the Graph-side Exchange backend (D-004) go through this; the direct client is the
alternative to a generated SDK (D-002).
Done when: the paging, throttling and batch behavior is covered by fixture tests that run offline, `call`
reaches a live endpoint after a `login`, and no fixture carries a real tenant's data (`check-fixtures.py`).
