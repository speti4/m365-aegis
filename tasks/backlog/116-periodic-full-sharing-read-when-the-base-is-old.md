---
status: ready
size: S
origin: wip/099-incremental-sharing-read-from-the-stored-delta-links.md
created: 2026-11-01
milestone: M5 Snapshot and views
---

# A periodic full sharing read when the incremental base is older than a configured age

What: `snapshot take` reads the sharing domain whole on its own when the base snapshot of the incremental read (task
099) is older than an age set in the profile (a week by default), and the manifest names the reason; `--full` keeps
forcing it.
Why: the delta under `Sites.Read.All`, without `deltashowsharingchanges`, may miss a change that touched a permission
only; a nightly incremental run needs a net under it, and the administrator should not have to remember a weekly flag.
Done when: over the fixtures a base older than the age makes the take read every drive whole with the reason in the
manifest, a younger base stays incremental, and the usage guide states the rule and the setting.
