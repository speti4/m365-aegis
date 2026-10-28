---
status: ready
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Audit lookups: sign-ins per user and failures, expiring application secrets, the directory audit

What: `signin list --user <upn> [--failures] [--days N]` over `auditLogs/signIns`; `secrets expiring <days>`
over the applications' `passwordCredentials` and `keyCredentials` (and the service principals'); `audit list
--days N [--activity …]` over `auditLogs/directoryAudits`; the time window and the paging in the client, the
filters in the command.
Why: the weekly report and the "what happened" questions of the audit domain, which the agent path answers
slowly and the admin centers one page at a time.
Done when: each command answers from fixtures and live, `secrets expiring 30` lists the credentials that expire
within a month with the application's name, and a tenant without Entra P1 gets a clear error for the sign-in
log instead of a stack trace.
