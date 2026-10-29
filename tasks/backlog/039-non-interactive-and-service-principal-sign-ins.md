---
status: idea
size: M
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
milestone: M1 Foundation
---

# Non-interactive and service principal sign-ins

What: `signin list` gains `--kind interactive|non-interactive|service-principal|managed-identity` over the beta
endpoint's `signInEventTypes` filter (v1.0 lists interactive user sign-ins only), through the client's beta
switch, with the beta dependency named in the hint and in `docs/development.md`.
Why: a leaked application secret or a token replay shows up in the non-interactive and service principal logs,
which the v1.0 answer never lists.
Done when: each kind answers from fixtures and live, and the command says which endpoint version answered.
