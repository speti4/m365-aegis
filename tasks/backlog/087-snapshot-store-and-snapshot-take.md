---
status: blocked
needs: D-011 (task 083, the snapshot store's format)
size: L
origin: session 2026-10-30
created: 2026-10-30
milestone: M5 Snapshot and views
---

# The snapshot store and `snapshot take`: groups, sites, sharing and mailbox permissions under the home folder

What: the store D-011 chose under the home folder (D-005: never in the repo) and the `snapshot take` command that
fills it over the reads the Core already has: groups with members and owners (direct and transitive), sites and their
sharing links, mailbox permissions in both directions; `snapshot list` and `snapshot show` over the snapshots kept, the
retention D-011 set, and a documented scheduled run (Task Scheduler, the silent sign-in of D-003); the sharing-link
read comes from the spike of task 092.
Why: every M5 view reads the store, the access view of task 084 first; a nightly run is what makes the views answer
in a second instead of minutes.
Done when: `snapshot take` writes one snapshot over the fake module and the fixtures in the D-011 shape, a second run
adds a new one and prunes by the retention, the snapshots stay outside the repo, and task 084 reads one.
