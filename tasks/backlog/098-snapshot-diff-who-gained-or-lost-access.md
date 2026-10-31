---
status: idea
size: M
origin: wip/084-access-view-for-one-user-from-the-snapshot.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# `snapshot diff <a> <b>`: who gained or lost access between two snapshots

What: a read over two snapshots of the store (D-011: the second file attached to the first) that lists, per domain,
what appeared and what disappeared: memberships, owners, sharing links and their grantees, mailbox grants, calendar
permissions and forwarding; `<a>` and `<b>` are snapshot names or relative picks (`latest`, `yesterday`, a date), the
envelope names both snapshots and their ages.
Why: the change view M5 promised after the access view; the maintainer parked it on 2026-10-30 until the store and the
view existed, both landed in the tenth round.
Done when: over two fixture snapshots the diff lists one added and one removed row in each domain, a diff of a snapshot
with itself is empty, and the command answers under a second from two tenant-sized files.
