---
status: idea
size: S
origin: wip/083-spike-snapshot-store-format-and-size.md
created: 2026-10-30
milestone: M5 Snapshot and views
---

# Spike: the sharing-link read for the snapshot, its consent and its time over the tenant

What: a measured spike of the one read the snapshot of task 087 lacks: the shared items and their grantees per site
through a drive's `delta` with `Prefer: hierarchicalsharing` and each marked item's `permissions`, the group behind a
site, whether OneDrives are in or out, which consent it needs (`Sites.FullControl.All` is the documented one, which the
app registration does not have) and how long it takes over the tenant's sites; the answer is a note in D-011 or a
decision of its own, not code.
Why: D-011 assumes about 170,000 shared items at most and a read the Core has no route for yet; a route that needs a
consent the tenant refuses changes the store's scope before task 087 builds it.
Done when: the route, the consent and the measured time over a few live sites are written down, and task 087's text
names the route it builds.
