---
status: blocked
needs: D-011 (task 083, the snapshot store's format)
size: M
origin: session 2026-10-30
created: 2026-10-30
milestone: M5 Snapshot and views
---

# `access <user>`: which groups, sites and mailboxes one user reaches, from the snapshot

What: a cross-workload read, `access <user>`, that answers from the snapshot store of D-011: the groups the user is a
member of (direct and transitive), the sites and sharing links those groups and the user reach, and the mailboxes the
user holds a right on; the first snapshot it reads is taken by a `snapshot take` command this task adds in the shape
D-011 chose, over the reads the Core already has.
Why: the "who can reach what" view is the gap no free tool fills (D-001, docs/origin.md); live it costs minutes, from
a snapshot it answers in a second.
Done when: `access alice@contoso.com` answers from a fixture snapshot in under a second with the envelope and the
table, `snapshot take` writes one over the fake module and the fixtures, and the snapshot never enters the repo.
