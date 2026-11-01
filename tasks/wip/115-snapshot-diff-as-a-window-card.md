---
size: M
origin: wip/098-snapshot-diff-who-gained-or-lost-access.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 115 — The snapshot diff as a window card

## Context

A card that picks two snapshots of the store (the newest and the one before it by default) and shows the diff of
task 098 per domain, the added and the removed rows, over a bridge request on the same Core read; no live call.
Why: the change list is written for the administrator at the command line today; the support colleague who asks "who
got access since yesterday" works in the window.
Done when: over the mock transport the card shows one added and one removed row per domain from two fixture
snapshots, says plainly when fewer than two snapshots exist, a Vitest test holds it over a bridge sample, and the usage
guide has the card.
Twelfth round of 2026-11-01, on one branch with task 114; the task session owns the steps below and may reshape them.
The parallel window bundle (tasks 110, 112, 113) adds a user picker and the domain names to the wording map: this card
registers itself without reshaping the cards' shared files more than the registration needs, and the second of the two
bundles to merge resolves the overlap.

## Docs that move

- `docs/usage.md`: the window's cards section gets the diff card
- `tests/contracts/`: the diff request and its answer, and the "fewer than two snapshots" answer
- `README.md`: the window's feature list, if it names the cards

## Not in this change

- A `--user` filter on the card (the read of task 114 may be wired later)
- Taking a snapshot from the window

## Steps

1. [x] The bridge request over the diff read (two picks, default newest and the one before), the answer with both
       snapshots' ages and the per-domain rows, the plain answer when fewer than two snapshots exist; the contract
       samples; the Core and Gui tests hold it
2. [x] The card: the two snapshots, the per-domain added and removed rows, the warnings in the window's words, the plain
       state; the mock transport answers from the sample
3. [x] Vitest over the bridge sample, 100% coverage held; Biome and the typecheck clean; the GUI `--smoke` passes
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-11-01 — promoted on `task/114-snapshot-diff-user-and-card` by the main session
- 2026-11-01 — Core: `SnapshotDiff.CompareWithThePrevious(home, profile, from, to, user, now)`: `to` the newest unless
  named, `from` the newest readable snapshot taken before `to` unless named (a damaged file skipped), null when there
  is none, so fewer than two to compare. The bridge: `snapshot-diff` with `{from?, to?, user?}`
  (`BridgeMessageType.SnapshotDiff`, `SnapshotDiffRequest`), answered with `snapshot-diff-report` (the core's
  `SnapshotDiffReport` as `snapshot diff --json` carries it) or `too-few-snapshots` (`TooFewSnapshotsInfo`: the
  profile, its count of readable snapshots, "no snapshot … yet", "only one snapshot … so far" or "no snapshot …
  before the one picked", and the window's hint, which names no command); a pick that is none or a user neither
  snapshot knows is the error of its kind with the window's hint. `BridgeSnapshotDiffTests` (Core) hold the request
  and the replies to `snapshot-diff.json`, `snapshot-diff-report.json` (the fixture snapshot and its changed copy, two
  hours after the second take), `snapshot-diff-report.user.json` (bob) and `too-few-snapshots.json`, with no broker
  call and no Graph request, plus one snapshot, nothing before a named one, two named picks, the errors and no
  profile.
- 2026-11-01 — the card: `components/snapshot-diff-card.tsx`, "Access changes", after "User access" on the page;
  "From", "To" and "Only this user" (each sent only when filled), "Compare", then the two snapshots' times and ages
  with the counts, the warnings ("Good to know", through the wording map), a table per part that changed (change,
  what, who, how, and "through" for one user), a line for the parts without a change and one for the parts left out;
  `too-few-snapshots` is a plain "Nothing to compare yet" notice, an `error` the page's toast; no sign-in needed. The
  parts' names are the card's own map (`partName`). `protocol.ts` reads both replies; the browser mock answers from
  `bridge/mock-snapshot-diff.ts` (the whole diff, bob's, the plain state under `?mock=no-snapshot`), held equal to the
  three samples by a Vitest test.
- 2026-11-01 — docs: `docs/usage.md` "### Access changes" under "The window" (and the card order of
  `UsageGuideTests`), `README.md`'s list of the window's cards, `docs/development.md` (the `snapshot-diff` message,
  "The access changes card").
- 2026-11-01 — after merging `origin/main` (#151, the close commit 443d956 and #152, the window bundle): the six
  bridge lists that both bundles extended keep both (`user-search` and `snapshot-diff`); `isUser` reuses #152's
  `userNames`. The card's own part names are gone: `diffPart` in `plan-wording.ts` names a part by task 113's
  `snapshotPart` and one user's `sharedItems` and `calendars` by the part they read. #152's check that no card warning
  starts with a store identifier caught the diff's warnings, so the wording map now rewords the diff's partial warning
  ("SharePoint sites: read only in part in the snapshot … so rows of Shared files may show as added or removed without
  a change") and its left-out warning ("Groups: not compared; Nested group memberships not read in the snapshot …").
  Git's rename detection carried 114 and 115 into `done/` with the closed tasks; they are back in `wip/`.

## Deviations from the plan

- The card offers the `--user` filter of task 114 ("Only this user"): the request carries it to the same core read,
  so it cost one field and one sample.
- The picks are text fields (a snapshot's name, `latest`, `yesterday` or a date), not a list of the snapshots: a list
  needs a request of its own (a follow-up).
- The parts' names come from the window bundle's map (task 113) once it landed, not from a map of the card's own; the
  diff's warnings are reworded in the same wording map, which this change extends by two rewordings and `diffPart`.

## Verification

- 2026-11-01 — Core: `BridgeSnapshotDiffTests` 7 green (the default request is the `snapshot-diff-report.json` sample
  with no broker call and no Graph request, bob's the `.user` variant, no snapshot the `too-few-snapshots.json`
  sample, one snapshot and nothing before a named one with their counts, two named picks, the errors with the window's
  hints, no profile); `BridgeProtocolTests` green with the four new samples.
- 2026-11-01 — Vitest: `snapshot-diff-card.test.tsx` over `MockTransport` (the two snapshots "2026-10-31 06:00 UTC
  (26 hour(s) ago)" and "2026-11-01 06:00 UTC (2 hour(s) ago)", 8 added and 6 removed, a table for each of the six
  parts with one added and one removed row or more, the partial warning reworded; bob's changes with "through Audit";
  "Nothing to compare yet" over `MockTransport({ snapshot: false })`; an unknown user as the toast's error; only the
  filled picks sent), the mock held equal to the three samples, `App.test.tsx` (the card on the page),
  `protocol.test.ts`; 224 tests, 100% statements, branches, functions and lines; Biome and `tsc` clean.
- 2026-11-01 — the whole local matrix in the worktree on the tree merged with `origin/main` (#151, 443d956, #152):
  restore `--locked-mode`, format, `npm ci`, lint, typecheck, `test:coverage`, the front end's build, the full build
  (0 warnings), `dotnet test` with coverlet 1630 passed, `check-coverage.py` 100% (Core 6662/6662 lines, 3433/3433
  branches; Cli 2157/2157, 548/548), `changelog.py --check`, `check-fixtures.py` self-test (17 cases) and scan (527
  files, 0 findings), `doctor --offline` exit 0, `whoami` exit 3 on an empty home, `release.ps1 -DryRun` with a
  throwaway certificate exit 0, and `m365-aegis-gui.exe --smoke` exit 0 in three runs of its own (ready in about
  0.5 s). Two flakes of this machine while other sessions built in parallel, neither in this change's code: the
  sidecar's `AChildThatHangsTimesOut…` test (a 3 s budget for a scripted pwsh child) failed in three full runs and
  passed alone and in the last full run, and CI's `dotnet` job on the branch was green; `--smoke` inside a batch with
  every stream redirected to a file exited 3 twice and passed once.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
