---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 084 — `access <user>`: which groups, sites and mailboxes one user reaches, from the snapshot

## Context

A cross-workload read, `access <user>`, that answers from the snapshot store of D-011: the groups the user is a member
of (direct and transitive), the sites and sharing links those groups and the user reach, and the mailboxes the user
holds a right on; it reads the snapshots that `snapshot take` of task 087 writes and adds no read of its own.
Why: the "who can reach what" view is the gap no free tool fills (D-001, docs/origin.md); live it costs minutes, from
a snapshot it answers in a second.
Done when: `access alice@contoso.com` answers from a fixture snapshot of task 087 in under a second with the envelope
and the table, names the snapshot's date, and fails with a hint when no snapshot exists.
Tenth round of 2026-10-31, on one branch with task 087 and after it; the task session owns the steps below and may
reshape them. D-011's "query path" section is the contract: the newest complete snapshot of the profile opened
read-only, the user by user principal name, mail or object id, one statement per section, the organization-wide and
anonymous links and the calendars' `Default` entries as counts, the snapshot's time in the envelope and a warning when
it is older than 36 hours. The live timing over a tenant snapshot is the main session's, at the end of the round.

## Docs that move

- `docs/usage.md`: the `access <user>` entry, with the hint shown when no snapshot exists
- `docs/development.md`, "Snapshot": the view's query path and its time budget
- `README.md`: the access view in the feature list, under the snapshot heading of task 087

## Not in this change

- `snapshot diff` (returns as a proposal at this task's close, the maintainer's call of 2026-10-30)
- A window card for the access view (an M5 follow-up, proposed at the close if the maintainer wants it)
- Any live read: the view answers from the store only

## Steps

1. [ ] The query path of D-011 over the store of task 087: the user resolved by UPN, mail or object id, the groups,
       the sites, the shared items and the counts of the everyone-reaching links and `Default` entries, the mailbox
       grants and calendar permissions, one statement per section
2. [ ] The command: `access <user>` with the envelope and the table (D-006), the snapshot's time in the envelope, the
       36-hour warning, the hint and its exit code when no snapshot exists
3. [ ] Tests over the fixture snapshot: the answer, the date, the hint; the timing under a second in a cold process
       (a measurement in the Verification)
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/087-snapshot-store` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
