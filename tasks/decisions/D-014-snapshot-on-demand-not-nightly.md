---
status: decided
date: 2026-11-01
cost: cheap
sources: [wip/116-periodic-full-sharing-read-when-the-base-is-old.md, D-011-snapshot-store-format.md, tasks/roadmap.md]
---

# D-014 — The snapshot is taken on demand; a schedule is an option, not the model

## Context

The roadmap's M5 and the snapshot documentation were written around a nightly take: a Task Scheduler task every
morning, the access and diff views answering from the morning's snapshot. Planning task 116 (the periodic whole read
of the sharing domain) the maintainer challenged the premise: the tool runs ad hoc on the administrators' own
machines, never as a service, and no machine is on every night. The question was what the snapshot part is worth
without a schedule, and whether to keep, freeze or drop it.

What only the snapshot gives, live or not: one user's reach across groups, sites, shared files, mailbox rights and
calendar permissions in under a second (the shared files and the calendars need a scan of the whole tenant, about
20 and 4.5 minutes live, with no per-user query); the difference between two points in time (who gained or lost
access), which no live read can answer; the links and calendars that reach everyone; and an answer for a support
colleague who holds none of the permissions the scan needs. A take never needed a running process: it is a batch of
minutes, started by hand or by a scheduled task on a machine that happens to be on.

## Decision

The snapshot part stays, in an on-demand frame: a take comes before the question ("take one on Monday, ask all
week", or before a review or an offboarding), and the usage guide says so first; the scheduled take stays in the
guide as the option it is, for a computer that is on every morning anyway and for the diff card's day-before. The
roadmap's M5 names a snapshot "taken on demand or on a schedule". The age rule of task 116 is kept, because it
matters more in an ad hoc chain, where an incremental take may start from a base weeks old. The bridge's hints that
suggest a schedule stay as they are: a schedule remains the easy way to always have a snapshot to compare with.
Decided by the maintainer in the session of 2026-11-01.

## Not chosen

- **Freeze M5**: keep the code and the cards, drop tasks 116 and 117 — the code would cost its maintenance without
  the two changes that make an ad hoc take safer and shorter.
- **Drop the snapshot part**: remove the store, the five commands, the two cards and D-011 — it would lose the diff,
  the shared-file and calendar overview and "reaches everyone", which nothing else in the tool answers.
- **Make the schedule mandatory** (the nightly model as written): nobody's machine runs every night, so the model
  would be false the day it shipped.
