---
status: idea
size: S
origin: done/048-mailbox-list-through-the-admin-api.md
created: 2026-10-30
milestone: M2 Exchange
---

# `mailbox forwarders` through the Admin API

What: `mailbox forwarders` reads the Admin API's mailbox list with the three forwarding fields in `$select` and the
accepted domains from its `AcceptedDomain` endpoint, and starts the sidecar only for the recipient targets it has
to resolve.
Why: since task 048 the list runs without a sidecar, and the forwarders read is the same list with three more
fields; the 2.4 s cold connect is paid for the recipient targets alone.
Done when: `mailbox forwarders` answers from fixtures and live with backend `admin-api` when no forwarder targets
a recipient, `admin-api+pwsh` otherwise, and the layer table names it.
