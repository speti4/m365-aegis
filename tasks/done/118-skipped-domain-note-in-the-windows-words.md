---
size: S
origin: wip/113-window-names-for-the-snapshot-parts-in-the-access-cards-warnings.md
created: 2026-11-01
started: 2026-11-01
closed: 2026-11-01
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

1. [x] A per-part note for a skipped part in `components/plan-wording.ts` (`skippedPartNotes`), the "was not read"
       rewording uses it for a part it knows and keeps the take's note for one it does not
2. [x] The Vitest test (`plan-wording.test.tsx`) forbids a permission name (`Sites.Read.All`, `Exchange.Manage`) and a
       `task NNN` reference over the reply samples, the access views and the browser mock's cards, and reads the
       take's skipped parts from `SnapshotTake.cs` so a new skipped part without a window note fails; 100% coverage
       held; Biome and the typecheck clean
3. [x] `docs/development.md` moved; the Verification dated; `tasks_check` clean

## Log

- 2026-11-01 — promoted on `task/118-skipped-domain-note-in-the-windows-words` by the main session
- 2026-11-01 — `skippedPartNotes` and `skippedPartNote(part, note)` in `components/plan-wording.ts`: oneDrive "other
  people's OneDrive files are not in the snapshot; ask the administrator if you need them"; the "was not read"
  rewording shows the window's note for a part the map knows and the take's for one it does not (an own-property
  check, as `snapshotPart`). The card reads "OneDrive: not read in this snapshot; other people's OneDrive files are
  not in the snapshot; ask the administrator if you need them." The mock and the bridge sample keep the take's note,
  so the test proves the rewording over the core's text.
- 2026-11-01 — the test: `skippedParts()` reads each `new SnapshotDomain("…", SnapshotDomainStatus.Skipped, …, "<note>")`
  of `SnapshotTake.cs` (1, equal to the map's keys; a new skipped part without a note fails); two new forbidden shapes,
  `permission` (`<Word>.Read|ReadWrite|ReadBasic|Manage…`) and `taskReference` (`task NNN`), over every string a card
  shows in the reply samples, the access views and the mock's cards. The first run found the only legitimate
  permission names in the samples: the token scopes of `signed-in.json` (`tokens[].scopes[]`), which no component
  renders; the sweep exempts that one path with a comment.
- 2026-11-01 — `user-access-card.test.tsx` expects the window's note and no `Sites.Read.All` or `task NNN` on the card;
  `docs/development.md` has the paragraph.
- 2026-11-01 — merged in #159 (squash, CI green); closed on `main` by the main session: no follow-up of its own

## Deviations from the plan

- none

## Verification

- 2026-11-01 — `npm run test:coverage` (225 tests, 100% statements, branches, functions, lines), `npm run lint`,
  `npm run typecheck`, `npm run build` clean; `python -I scripts/changelog.py --check` and `check-fixtures.py`
  (self-test and the 528 files) clean; `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  `dotnet build -c Release -p:SkipFrontendBuild=true` and `dotnet test` passed. No C# change: the take's note in the
  manifest and on the command line is unchanged (`SnapshotTake.cs` untouched, the Core's `SnapshotAccessTests` hold the
  store's text).

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log (none)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-01`
