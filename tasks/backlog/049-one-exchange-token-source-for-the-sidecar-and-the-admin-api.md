---
status: idea
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
milestone: M2 Exchange
---

# One Exchange token source for the sidecar and the Admin API

What: a command that uses both layers (`mailbox access` since task 042) fetches the Exchange token twice, once for
the sidecar's connect and once for the Admin API client, through two parallel silent broker calls; one shared
source (`ExchangeBackends` or the `AuthService`) hands the same token and its expiry to both, and the renewal
before the expiry happens once.
Why: two silent calls per command are twice the broker's cache round trip and two log entries for one fact; the
window's long-lived session (task 045) makes the duplication permanent.
Done when: a `mailbox access` run shows one Exchange token acquisition in the verbose log, the sidecar's
`reconnect` and the Admin API's token refresh read the same expiry, and the tests hold the count.
