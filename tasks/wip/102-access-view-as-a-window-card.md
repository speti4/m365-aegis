---
size: M
origin: wip/084-access-view-for-one-user-from-the-snapshot.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 102 — The access view as a window card

## Context

A card of the window that asks for a user (by name or address, the user lookup's picker) and shows what `access
<user>` of task 084 answers from the newest snapshot: the groups with their via groups, the sites, the shared items,
the mailboxes and calendars, the counts of what everyone reaches, the snapshot's age and its warnings; a bridge request
over the same Core read, no live call.
Why: "who can reach what" is the support team's question, and the support colleague works in the window, not the
command line (the M4 and M5 done-whens).
Done when: over the mock transport the card answers for a fixture user with the snapshot's date, says so plainly when no
snapshot exists, a Vitest test holds it over a bridge sample, and the usage guide has the card.
Eleventh round of 2026-10-31, on one branch with tasks 103 and 104; the task session owns the steps below and may
reshape them. The access read of task 084 (`SnapshotAccess`) is used as it is: the snapshot bundles of the round (097,
098, 099, 100, 101) add to the store and the take, none of them changes the access read's shape.

## Docs that move

- `docs/usage.md`: the window's cards section gets the access card
- `tests/contracts/` (the bridge contract samples): the new request and its answer
- `README.md`: the window's feature list, if it names the cards

## Not in this change

- A live (non-snapshot) access read from the window
- The `snapshot take` from the window, or a card that shows the snapshot list
- The diff view (task 098) as a card

## Steps

1. [x] The bridge request (`BridgeProtocol`, `BridgeDispatcher`) over `SnapshotAccess`, read-only, with the no-snapshot
       answer; the contract sample under `tests/contracts/`; the Core and Gui tests hold it
2. [x] The card: the user picker of the user lookup, the sections of the access view, the snapshot's age and its
       warnings, the plain "no snapshot" state; the mock transport answers from the sample
3. [x] Vitest over the bridge sample, 100% coverage held; Biome and the typecheck clean
4. [x] Docs under *Docs that move*; the Verification dated (the GUI `--smoke` too); `tasks_check` without `write`
       clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session
- 2026-10-31 — the bridge: `access` with `{"user": …}` (`BridgeMessageType.Access`, `AccessRequest`), answered with
  `access-report` (the core's `AccessReport` as `access <user> --json` carries it) or `no-snapshot` (`NoSnapshotInfo`:
  the profile, the core's sentence, the window's hint "ask the administrator to take a snapshot on this computer, or to
  schedule one every morning"); `BridgeDispatcher.Access` runs `SnapshotAccess.ForUser` unchanged over the newest
  snapshot of the window's profile (`Auth.Profiles`, so a missing profile gets the window's hint), the age on
  `BridgeServices.Clock`. Nothing under `Snapshots/` changed. `BridgeAccessTests` (Core) hold the request and both
  replies to `access.json`, `access-report.json` (dave over the fixture snapshot, 2 hours after the take) and
  `no-snapshot.json`, with no broker call and no Graph request, plus an unknown user (`not-found`, the window's hint),
  a missing user (`usage`), no profile (`auth`) and a profile name that cannot be a folder (`usage`)
- 2026-10-31 — the card: `components/user-access-card.tsx`, "User access", after "Mailbox access" on the page; a user,
  "Look up", then the snapshot's time (UTC) and age, the warnings ("Good to know", through the wording map), five
  sections (groups with "through <via>", sites with the role, shared files, mailboxes, calendars), each a table or a
  line when empty, and the counts of what reaches everyone; `no-snapshot` is a plain "No snapshot yet" notice, an
  `error` the page's toast; the card does not wait for a sign-in. The protocol (`protocol.ts`) reads both replies; the
  browser mock answers dave from `bridge/mock-access.ts`, which a Vitest test holds equal to the sample, and
  `?mock=no-snapshot` starts it without a snapshot
- 2026-10-31 — docs: `docs/usage.md` "### User access" under "The window" (and the card order of `UsageGuideTests`),
  `README.md`'s list of the window's cards, `docs/development.md` (the `access` message, "The user access card")

## Deviations from the plan

- The card's user field is a plain text field like the mailbox access card's user mode: task 077 has no picker
  component to reuse (it split the reverse lookup's read), and a picker that suggests users from the snapshot is a
  follow-up
- No snapshot is a reply of its own, `no-snapshot`, a state of the card as `signed-out` is one of the sign-in card,
  not an `error`; a user the snapshot does not know stays the `not-found` error with the window's hint of the kind
  (the core's names `snapshot take`)
- The variant sample `plan.group-owners-remove.json` (task 104) is the first second sample of a type: the samples
  stay named after their type, a variant after a dot
- `gui/web/src/test/dom.ts`: `settle()` lets four more turns of the real clock pass after its wait. The applied-writes
  tests failed now and then on `main` already (2 of 5 runs of the file before this change: the access card's second
  request answered after the wait by chance); task 103 adds tests of the same chain, and six whole-suite runs after
  the change were green

## Verification

- 2026-10-31 — Core: `BridgeAccessTests` 7 green, `BridgeProtocolTests` green with the new samples; `dotnet test` 1544
  passed; coverage gate 100% lines and branches (Core 6026/6026, 3179/3179; Cli 2074/2074, 520/520)
- 2026-10-31 — Vitest: `user-access-card.test.tsx` (dave over the mock with the sample's rows, the date
  "2026-10-31 06:00 UTC, 2 hour(s) ago" and the reworded warnings; "No snapshot yet" over `MockTransport({ snapshot:
  false })`; an unknown user as the toast's error), `App.test.tsx` (the card signed out), `protocol.test.ts`,
  `transport.test.ts`; 194 tests, 100% statements, branches, functions and lines; Biome and `tsc` clean
- 2026-10-31 — the browser mock in the dev server (`npm run dev`, then `?mock=no-snapshot`): the card for
  dave@contoso.com and the plain state, by screenshot; the dev server stopped after
- 2026-10-31 — the whole local matrix in the worktree: restore `--locked-mode`, format, build, test, the changelog and
  fixture checks, the coverage gate, the `gui/web` block, the full build with the front end, `doctor --offline` exit 0,
  `whoami` exit 3 on an empty `M365AEGIS_HOME`, `m365-aegis-gui.exe --smoke` exit 0 and `scripts/release.ps1 -DryRun`
  (the zip packed, nothing written under `dist`)

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
