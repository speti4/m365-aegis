---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Message trace through Graph v1.0

What: `trace --sender <address> | --recipient <address> [--days N]` over
`/admin/exchange/tracing/messageTraces` (the 10-day window, 90 days back, 100 requests per 5 minutes); the
`ExchangeMessageTrace.Read.All` scope and the one-time provisioning of the service principal documented in
`docs/development.md`; the throttling budget respected by the client.
Why: the first mailbox-domain question that Graph answers, so it needs no sidecar; the research found it GA in
v1.0 in 2026-02.
Done when: a trace of a made-up message answers from fixtures, a live trace of a known message answers after
the provisioning step, and a window wider than 10 days is refused with a hint instead of an empty answer.
