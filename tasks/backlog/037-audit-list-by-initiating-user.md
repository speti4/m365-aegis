---
status: idea
size: S
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
milestone: M1 Foundation
---

# `audit list --user <upn>`: the directory audit by the initiating user

What: a `--user <upn>` option on `audit list` that filters `auditLogs/directoryAudits` on
`initiatedBy/user/userPrincipalName` (the documented filter), combinable with `--days`, `--since` and
`--activity`.
Why: "what did this admin change" is the question after an incident, and the full day's list of a busy tenant
is a thousand rows (the live round of 2026-10-29 hit the limit).
Done when: the option answers from fixtures and live, and the footer names the user.
