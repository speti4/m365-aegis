---
size: S
origin: wip/113-window-names-for-the-snapshot-parts-in-the-access-cards-warnings.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M4 Window
---

# 118 — A skipped snapshot domain's note in the window's words on the access cards

## Context

The warning the access cards show for a skipped domain (today OneDrive: "other users' OneDrives need a consent the
tool does not ask for (Sites.Read.All …; task 092)") is reworded through the wording map of tasks 090, 104 and 113 to
what the colleague can act on ("other people's OneDrive files are not in the snapshot; ask the administrator"), and the
Vitest test that forbids a command name on a card also forbids a task number and a permission name.
Why: the take's note is written for the administrator at the command line; a permission name and a task number on a
card are the same dead end as a flag.
Done when: over the bridge samples no card renders a permission name or a `task NNN` reference, the test holds it, and
the command line's note is unchanged.
Thirteenth round of 2026-11-01, worked in the main session on its own branch (no worktree, no task session).

## Docs that move

- `docs/development.md`, "The snapshot's parts in the window's words": the skipped part's note is the window's own
  for a part the map knows, the take's for one it does not; the test's two new forbidden shapes

## Not in this change

- The take's note in the manifest and on the command line (`SnapshotTake.cs`)
- The diff card's "left out" warnings (task 114), which carry no note

## Steps

1. [ ] A per-part note for a skipped part in `components/plan-wording.ts` (`skippedPartNotes`), the "was not read"
       rewording uses it for a part it knows and keeps the take's note for one it does not
2. [ ] The Vitest test (`plan-wording.test.tsx`) forbids a permission name (`Sites.Read.All`, `Exchange.Manage`) and a
       `task NNN` reference over the reply samples, the access views and the browser mock's cards, and reads the
       take's skipped parts from `SnapshotTake.cs` so a new skipped part without a window note fails; 100% coverage
       held; Biome and the typecheck clean
3. [ ] `docs/development.md` moved; the Verification dated; `tasks_check` clean

## Log

- 2026-11-01 — promoted on `task/118-skipped-domain-note-in-the-windows-words` by the main session

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
