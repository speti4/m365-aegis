---
status: ready
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Group, Teams and SharePoint lookups: members and owners, groups without owners, team list, site list

What: the first read commands of the groups domain over the Graph client: `group members <name or id>`,
`group owners <name or id>`, `group owners-missing` (every group with no owner), `team list`, `site list`; a
name resolves through `$search` or `startsWith` with `ConsistencyLevel: eventual`, an ambiguous name lists the
candidates and exits 4 with a hint. Each command renders through the output contract.
Why: the daily questions the admin centers answer slowly, and the first real use of the client and the contract.
Done when: each command answers from fixtures in the tests and from the live tenant after a `login`, and
`group owners-missing` over the tenant finishes in well under a second plus the network.
