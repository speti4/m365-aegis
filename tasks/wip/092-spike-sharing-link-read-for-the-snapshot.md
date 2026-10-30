---
size: S
origin: wip/083-spike-snapshot-store-format-and-size.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 092 — Spike: the sharing-link read for the snapshot, its consent and its time over the tenant

## Context

A measured spike of the one read the snapshot of task 087 lacks: the shared items and their grantees per site through
a drive's `delta` with `Prefer: hierarchicalsharing` and each marked item's `permissions`, the group behind a site,
whether OneDrives are in or out, and how long it takes over the tenant's sites, under `Sites.Read.All` only: the
maintainer declined `Sites.FullControl.All` on 2026-10-31 as too strong a consent for this tool, so the spike measures
what the read-only scope gives (which items and permissions it returns, what it misses) and names the gap; the answer
is a note in D-011 or a decision of its own, not code.
Why: D-011 assumes about 170,000 shared items at most and a read the Core has no route for yet; what the read-only
scope cannot reach narrows the store's sharing scope before task 087 builds it.
Done when: the route, the consent and the measured time over a few live sites are written down, and task 087's text
names the route it builds.
Tenth round of 2026-10-31, in the main session on `main` (a spike with no code to build, the one signed-in round of
the round): the measurements run with the broker's cached token through a throwaway script in the scratchpad, the
tenant's values stay in a scratch file, and only counts, timings and scrubbed shapes enter this file and the decision.

## Docs that move

- `tasks/decisions/D-011-snapshot-store-format.md`: the route and the scope's gap as a dated note, or a new decision
  if the scope narrows the store's sharing domain
- `docs/development.md`, "Snapshot": one line on the sharing read's scope (written by task 087 with the route)

## Not in this change

- The read's code in the Core and the take (task 087 builds the route this spike names)
- A consent change of the app registration (`Sites.FullControl.All` is declined)

## Steps

1. [ ] The route on paper: the drive's `delta` with `Prefer: hierarchicalsharing`, the marked items' `permissions`, the
       group behind a site (`/sites/{id}/drive` and `/groups/{id}/sites/root`), what the Graph reference says about
       `Sites.Read.All` for each call
2. [ ] The measurement over a few live sites with the cached token: items returned, items marked as shared, the
       permissions read per marked item and what came back empty or forbidden, the time per site, OneDrives in or out
3. [ ] The extrapolation to the tenant's 111 sites and the gap named: what the read-only scope misses and what the
       store's sharing domain holds under it
4. [ ] D-011's note (or a new decision proposed to the maintainer), the route sent to the task session of 087, the
       Verification dated; `tasks_check` with `write` at the close on `main`

## Log

- 2026-10-31 — promoted on `main` by the main session (a spike with no build; the admin-bypass housekeeping commit)

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
