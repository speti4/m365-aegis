---
status: idea
size: M
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
milestone: M5 Snapshot and views
---

# The snapshot diff as a window card

What: a card that picks two snapshots of the store (the newest and the one before it by default) and shows the diff of
task 098 per domain, the added and the removed rows, over a bridge request on the same Core read; no live call.
Why: the change list is written for the administrator at the command line today; the support colleague who asks "who
got access since yesterday" works in the window.
Done when: over the mock transport the card shows one added and one removed row per domain from two fixture
snapshots, says plainly when fewer than two snapshots exist, a Vitest test holds it over a bridge sample, and the usage
guide has the card.
