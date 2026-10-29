---
status: idea
size: S
origin: done/007-message-trace-through-graph.md
created: 2026-10-29
milestone: M1 Foundation
---

# `trace --status` and `--subject` filters

What: `--status <delivered|failed|pending|…>` and `--subject <text>` on `trace`, sent as `$filter` terms where
the endpoint supports them and applied client-side where it does not, with the choice stated in the hint and
in `docs/development.md`.
Why: a busy sender's 10-day trace is hundreds of rows; the support question is the failed ones or one subject.
Done when: both filters answer from fixtures and live, and a filter the endpoint refuses is applied client-side
rather than answered with a 400.
