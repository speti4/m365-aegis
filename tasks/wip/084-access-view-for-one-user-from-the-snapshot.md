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

1. [x] The query path of D-011 over the store of task 087: the user resolved by UPN, mail or object id, the groups,
       the sites, the shared items and the counts of the everyone-reaching links and `Default` entries, the mailbox
       grants and calendar permissions, one statement per section
2. [x] The command: `access <user>` with the envelope and the table (D-006), the snapshot's time in the envelope, the
       36-hour warning, the hint and its exit code when no snapshot exists
3. [x] Tests over the fixture snapshot: the answer, the date, the hint; the timing under a second in a cold process
       (a measurement in the Verification)
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/087-snapshot-store` by the main session
- 2026-10-31 — `SnapshotAccess.ForUser` (Core, `Snapshots`), D-011's query path: the newest readable snapshot of the
  profile (another schema version or a damaged file is skipped), opened read-only; the user by object id, user
  principal name or address among the users and the external addresses; one statement each for the groups (with the
  via groups), the sites (of the user's groups, and of the groups the user owns, as `member` or `owner`), the shared
  items of the user or the groups, the counts of what reaches everyone (organization-wide and anonymous links, calendars
  whose `Default` gives more than free/busy, whose `Anonymous` gives anything), the mailbox grants and the calendar
  permissions of the user or the groups. The answer names the snapshot and its age, warns past 36 hours and for each
  domain the snapshot lacks in part or whole.
- 2026-10-31 — `access <user>` on the command line (`local`, no sign-in), the envelope and a table of a row per group,
  site, shared item, mailbox right and calendar permission with what it comes through; no snapshot or no such user is
  exit code 4 with a hint (`run m365-aegis snapshot take first`).
- 2026-10-31 — tests over the fixture snapshot of task 087: `SnapshotAccessTests` (dave through Finance EU and Finance
  Staff, alice by object id and by address, an external address, the 36-hour warning, the newest readable file, the
  three failures), `SnapshotCommandTests` (the child process, both renderings, the hint), `TableViewTests`.
- 2026-10-31 — docs: `docs/usage.md` ("What one user reaches"), the access view's query path and budget in
  `docs/development.md` ("Snapshots"), the README's snapshot section.

## Deviations from the plan

- An external address (a sharing link's email-only grantee) can be asked for too; it answers with its shared items.
- The calendars that reach everyone are counted as D-011 says for `Default` (above free/busy), and `Anonymous` entries
  that give anything are counted beside them.

## Verification

- 2026-10-31: `access dave@contoso.com --json` over the fixture snapshot in a cold process, five runs of the built exe:
  157 to 165 ms for the whole process, 82 to 86 ms of it the command (`meta.elapsedMs`); under D-011's second.
  `SnapshotCommandTests` holds the child process under three seconds for a CI runner.
- 2026-10-31: dave's answer: Finance and Finance Staff through Finance EU, Finance EU and Project Phoenix directly; the
  sites of Finance and Project Phoenix; `Reports` through Finance EU, `Roadmap.pptx` through Finance Staff and directly;
  Reception's FullAccess and carol's calendar through Finance Staff; 1 organization-wide link, 1 anonymous, 1 calendar
  open to every user, 1 to anonymous; the warnings name the partial `sites` and the skipped `oneDrive`.
- 2026-10-31: without a snapshot, `access` ends with exit 4, `error: no snapshot of the profile 'contoso' yet` and the
  take as its hint; the whole local matrix as in task 087's Verification.
- The live timing over a tenant snapshot is the main session's (the pull request's "Live check").

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
