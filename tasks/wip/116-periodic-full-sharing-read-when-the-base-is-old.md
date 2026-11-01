---
size: S
origin: wip/099-incremental-sharing-read-from-the-stored-delta-links.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 116 — A periodic full sharing read when the incremental base is older than a configured age

## Context

`snapshot take` reads the sharing domain whole on its own when the base snapshot of the incremental read (task 099) is
older than an age set in the profile (a week by default), and the manifest names the reason; `--full` keeps forcing it.
Why: the delta under `Sites.Read.All`, without `deltashowsharingchanges`, may miss a change that touched a permission
only; a nightly incremental run needs a net under it, and the administrator should not have to remember a weekly flag.
Done when: over the fixtures a base older than the age makes the take read every drive whole with the reason in the
manifest, a younger base stays incremental, and the usage guide states the rule and the setting.
Thirteenth round of 2026-11-01, worked in the main session on its own branch (no worktree, no task session); the plan
session reshapes the steps below.

## Docs that move

- `docs/usage.md`: the incremental read's rule gains the age, the profile setting and the manifest's reason
- `docs/development.md`: the manifest's reason field, where the snapshot section describes the manifest
- `tasks/decisions/D-011-snapshot-store-format.md`: a note only if the manifest's format gains a field the decision lists

## Not in this change

- The `deltashowsharingchanges` preference and its consent (a different permission; stays out, as task 099 left it)
- A periodic full read of any other domain (they are read whole every take today)
- The retention rule of D-011

## Steps

1. [ ] The profile setting (its name, default a week) read by the take, and the base's age compared to it
2. [ ] The take reads every drive whole when the base is older, the manifest names the reason; `--full` unchanged
3. [ ] Fixtures and Core tests: an older base reads whole with the reason, a younger one stays incremental
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` clean

## Log

- 2026-11-01 — promoted on `task/116-periodic-full-sharing-read-when-the-base-is-old` by the main session

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
