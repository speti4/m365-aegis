---
size: S
origin: done/052-group-membership-and-owner-writes.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M3 Writes with safety
---

# 058 — The Graph `$ref` writes twenty to a `$batch` call

## Context

The apply of `group members add|remove` and `group owners add|remove` sends its `$ref` POST and DELETE requests
twenty to a `$batch` call (the Graph client's batching of task 003), each change's result read from its part of the
batch answer; a CSV of 100 members is 5 requests instead of 100.
Why: a bulk change (task 054) is one request per row today, which is slow and counts against throttling.
Done when: a plan of 100 member changes applies from recorded batch answers in 5 requests, a failed part reports
its own change and leaves the others applied, and the per-change results are the same as with single requests.
Part of the writes and plan records bundle (058, 061, 060, 062) of the round of 2026-10-30; the task session owns
the steps below and may reshape them. The live check of the round is `--dry-run` only (decided 2026-10-30): the
batched apply is proven on recorded batch answers in the fixtures, nothing is applied in the tenant. The parallel
Exchange and window bundle (055, 059, 056, 057) does not touch `GroupWrites` or the Graph client.

## Docs that move

- `docs/development.md`, "Writes and plans": the apply in batches of twenty, a failed part's result
- `README.md`: the bundle's own heading

## Not in this change

- batching of the reads (the lookups page on their own)
- a retry of a throttled part beyond what the Graph client does today

## Steps

1. [x] The apply of the four group writes over the Graph client's `$batch` (twenty per call), each change's result
   from its part; a failed part marks its own change, the others stay applied
2. [x] Recorded and scrubbed batch answers in the fixtures (a full batch, a batch with one failed part)
3. [x] Core tests (100 changes in 5 requests, the failed part, the results equal to single requests) and the Cli
   snapshots unchanged
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the writes and plan records bundle (058, 061, 060, 062)
- 2026-10-30 — `Core/Operations/WritePlan.cs`: a change's step is `PlanStep.Alone` (the step of task 051) or
  `PlanStep.Batched(batch, item)`, an item of a `PlanBatch<T>` that sends up to its size of items in one request and
  answers one outcome per item (null applied, else the failure); `WritePlan.WithSteps` and the builder's
  `Change(change, step)`. `PlanGuard` sends the neighboring changes of one batch together, up to its size, each with
  its own outcome; a request that fails as a whole fails each of its changes, and a stopping failure (auth, sidecar,
  allow-list, internal) stops the apply after that request, the rest `skipped`; a bug or a cancellation during a batch
  marks its changes `internal`/`cancelled` as for a single step.
- 2026-10-30 — `Groups/GroupWrites.cs`: `RefBatch(graph)` is the plan's one batch, twenty to a call
  (`GraphClient.MaxBatchSize`), over `GraphClient.BatchAsync` (its throttled-item retry included); an item's outcome is
  `GraphBatchResponse.EnsureSuccess`'s failure, so a refused part has the message and the hint a single request would
  have (`… answered 403 to POST /groups/…/members/$ref: Authorization_RequestDenied: …`, the permission in the hint).
- 2026-10-30 — fixtures: `tests/fixtures/batches/ref-writes-twenty.json` (twenty `204` parts, out of order) and
  `ref-writes-one-failed.json` (part 7 a 403 with its error body); the Core test's `RecordedBatchTransport` answers a
  call's `n`-th item from part `n` and every other request from `tests/fixtures/graph`; request ids `…021`–`…025`
  (the batch calls) and `…029` (inside the 403 body). The existing group write fixtures answer the batch item by item.
- 2026-10-30 — tests: `GroupWritesBatchTests` (a hundred removals: five `$batch` calls of items 1–20, five request ids
  in the log, 99 applied and change 87 failed `forbidden` with the single request's message and hint, exit 6, the
  record with 100 results; the Finance CSV through a batch: one call for the resolution and one for both writes, Eve's
  error equal to `SendAsync`'s for the same request), `PlanBatchTests` (neighbors grouped up to the size and split by a
  change sent alone; a batch call failing with auth fails its two changes and skips the rest; a batch that answers too
  few outcomes is an internal failure of each change), `GroupWritesTests` reading the writes from the batch items, the
  Cli's `YesAppliesThePlanAndWritesTheRecord` reading the batch call's request id (`…0fd`). The plan snapshots did not
  change.
- 2026-10-30 — docs: `docs/development.md` "Writes and plans" (the batches after the flags table, "In code", the group
  writes' bullet); `README.md` "Batched group writes, distribution lists, plan records and resume";
  `tests/fixtures/README.md` "The writes and plan records bundle".

- 2026-10-30 — closed by the main session on `main` after #26; no follow-up

## Deviations from the plan

- The batching is a guard concern (`PlanStep`, `PlanBatch`) rather than a loop inside `GroupWrites`: the guard keeps
  stopping and cancelling between requests, a resume (task 062) can keep a subset of the steps, and a change of another
  layer (task 061) sits between Graph changes in the same plan.
- The envelope's `meta.requestIds` carries the request id of each `$batch` call, not one per change: Graph's batch
  answer gives the call an id, and its parts carry none of their own.
- The hundred-change test applies a plan built over `GroupWrites.RefBatch` directly, against recorded batch answers
  read by a test transport, rather than a hundred-member group in `tests/fixtures/graph`: the made-up ids of the
  fixtures (`…0000NN`) leave too few free for a hundred members.

## Verification

- 2026-10-30 — the local matrix in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  the full build with the front end (0 warnings), `dotnet test` 758 passed (0 failed), `changelog.py --check`,
  `check-fixtures.py` (283 files, 0 findings), Pester 42 passed, PSScriptAnalyzer 0 findings over
  `src/M365Aegis.Core/Exchange/Sidecar`, `tests/sidecar` and `scripts`, the commit rules' self-test (38 cases); the
  `windows` job's steps in an empty home: `--version` 0, `--json doctor --offline` 0, `--json whoami` 3,
  `m365-aegis-gui --smoke` 0, `release.ps1 -DryRun` 0. `gui/web` is untouched.
- 2026-10-30 — against the recorded answers: a hundred member removals applied in five `$batch` calls, 99 applied and
  one failed `forbidden`, exit 6; `group members add --from-csv group-members-add.csv --yes` exit 6 as before (line 2
  applied, line 4 `forbidden`), now one `$batch` call for both writes.
- 2026-10-30 — live, the main session's signed-in round, dry run only: `group members add <Microsoft 365 group>
  <user> <user> --dry-run` exit 0, backend `graph`, 2 changes, 0 warnings, 3 request ids, 773 ms in the envelope: the
  plan of a Graph group keeps its round 4 shape. The batched apply itself is proven on the recorded batch answers;
  nothing was applied in the tenant.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
