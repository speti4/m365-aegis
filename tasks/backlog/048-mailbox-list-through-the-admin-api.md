---
status: idea
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
milestone: M2 Exchange
---

# `mailbox list` through the Admin API, and the usage report timed apart from the mailbox list

What: `mailbox list` reads the paged `Get-Mailbox` of the Admin API (every field the list shows, `$select` for
the table's columns) instead of starting a sidecar; and the `report` method of `mailbox size` times Graph's usage
report and the Admin API's mailbox list apart, so the Log says which of the two costs the 16 s measured on
2026-10-30 (the two ran side by side, unmeasured).
Why: the list is the most common mailbox read and pays the 2.4 s cold connect for nothing the REST layer cannot
give; the size default's time is unexplained until the two calls are measured apart.
Done when: `mailbox list` answers from fixtures and live with backend `admin-api` and no pwsh child, the two
timings are in the Log, and `docs/development.md`'s layer table names the list under the Admin API.
