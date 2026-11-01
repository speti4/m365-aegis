---
size: S
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
started: 2026-11-01
closed: 2026-11-01
plan: none
milestone: M4 Window
---

# 113 — The window's own names for the snapshot parts in the access card's warnings

## Context

The access card's warnings name the snapshot's domains in the window's words ("SharePoint sites", "OneDrive")
instead of the store's identifiers (`sites`, `oneDrive`), through the wording map of tasks 090 and 104.
Why: a domain identifier is the command line's word; the colleague who reads "sites is partial" on a card has no
command line to look it up in.
Done when: no card renders a bare domain identifier in a warning over the bridge samples, the Vitest dictionary test of
task 104 holds the new entries, and the command line's text is unchanged.
Twelfth round of 2026-11-01, on one branch with tasks 110 and 112; the task session owns the steps below and may reshape
them.

## Docs that move

- none (the wording map is the window's own)

## Not in this change

- The command line's domain names and warnings
- The take's own notes in the manifest

## Steps

1. [x] The domain names in the wording map (`plan-wording.ts` or its sibling for the access card), every domain of the
       take covered, the access card's warnings reworded through it
2. [x] The Vitest dictionary test of task 104 forbids a bare domain identifier on a card and holds the new entries;
       100% coverage held; Biome and the typecheck clean
3. [x] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/112-user-picker-and-window-words` by the main session
- 2026-11-01 — `snapshotPartNames` in `components/plan-wording.ts`, exported so a later card can name the parts the
  same way: users "Users", groups "Groups", mailboxes "Mailboxes", members "Group members", userGroups "Nested group
  memberships", owners "Group owners", teams "Teams", sites "SharePoint sites", sharing "Shared files" (the card's own
  section title), oneDrive "OneDrive", grants "Mailbox rights", folderPermissions "Calendar permissions", forwarding
  "Mail forwarding"; `snapshotPart` keeps the store's word for a part the map does not know (an own-property check, so
  `constructor` is no part).
- 2026-11-01 — the rewordings: the partial and the failed part start with the window's name ("SharePoint sites: read
  only in part in this snapshot; …"), and the skipped part (`<part> was not read: <note>`), which passed through
  unchanged before, becomes "OneDrive: not read in this snapshot; <the take's note>."; the part is captured as a word
  (`\w+`), so no other warning is taken for one.
- 2026-11-01 — the test (`plan-wording.test.tsx`) reads the parts from `SnapshotTake.cs` (each `DomainAsync("…"` and
  the skipped `new SnapshotDomain("…"`): 13, equal to the map's keys; each part through each of the three access
  warnings starts with its window name; a part's store word before "is partial", "could not be read" or "was not
  read" is forbidden over every reply sample, the access views and the browser mock's card, beside the flag and the
  command of task 104.
- 2026-11-01 — merged in #152 (squash); closed on `main` by the main session: no follow-up of its own

## Deviations from the plan

- none

## Verification

- 2026-11-01 — `npm run test:coverage` (211 tests, 100% statements, branches, functions, lines), `npm run lint`,
  `npm run typecheck`: the access card over the mock shows "SharePoint sites: read only in part in this snapshot; 1
  item(s) could not be read (forbidden 1), …" and "OneDrive: not read in this snapshot; other users' OneDrives need a
  consent …"; no reply sample, access view or mock card shows a bare part. The command line's text is the core's,
  unchanged: no C# change for this task, and the Core's `SnapshotAccessTests` still hold the store's words.
- 2026-11-01 — after `origin/main` was merged (#151, the take's manifest fields and per-domain counters): the test still
  reads 13 parts from `SnapshotTake.cs`, equal to the map; `npm run test:coverage` 213 tests, 100%.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (none; `docs/development.md` names the map)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-11-01` by the main session after the merge
