---
status: idea
size: S
origin: done/007-message-trace-through-graph.md
created: 2026-10-29
milestone: M1 Foundation
---

# Message trace details per recipient

What: `trace details <message id> [--recipient <address>]` over the trace endpoint's `getDetailsByRecipient`
function, listing the delivery steps of one message (the events with their timestamps, the status and the
detail text), through the Graph client and the output contract.
Why: the trace list says delivered or not; the support question is where a message stopped.
Done when: the details of a made-up message answer from fixtures and a live message's steps answer after one
sign-in, within the trace endpoint's throttling budget.
