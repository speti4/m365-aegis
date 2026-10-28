---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Spike: the Exchange Online Admin API v2.0 in the tenant, `Exchange.ManageV2` for a public client

What: a timeboxed try (half a day): is the preview Admin API enabled for the tenant ("not available in all
organizations"), can a public client acquire `Exchange.ManageV2`, and does one call
(`adminapi/v2.0/<tenant id>/OrganizationConfig` with `X-AnchorMailbox`) answer with the broker's token; the
shape and the `nextLink` expiry noted for the backend of D-004.
Why: the second layer of the Exchange reach exists only if the tenant and the token allow it; otherwise the
sidecar carries those six endpoints too.
Done when: a dated note in this task says yes or no with the response shape, and the M2 backlog is adjusted.
