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

## Deviations from the plan

- The card offers the `--user` filter of task 114 ("Only this user"): the request carries it to the same core read,
  so it cost one field and one sample.
- The picks are text fields (a snapshot's name, `latest`, `yesterday` or a date), not a list of the snapshots: a list
  needs a request of its own (a follow-up).
- The parts' names are the card's own map, not the window's wording map, which the parallel window bundle extends
  with the domain names; the two can be merged once both landed.
- The diff's warnings name the snapshot files and the core's domain names; no rewording was added to the shared
  wording map in this change (a follow-up).

## Verification

- <dated, concrete: the card over the mock transport, the Vitest run, the smoke run>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
