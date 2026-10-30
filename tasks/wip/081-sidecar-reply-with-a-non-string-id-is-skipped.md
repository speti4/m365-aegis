---
size: S
origin: done/070-full-line-and-branch-coverage-enforced-in-ci.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M2 Exchange
---

# 081 — A sidecar reply line whose `id` is not a string is skipped, not an internal error

## Context

`SidecarClient.TryReadReply` treats a reply line whose `id` is not a JSON string the way it treats any other
malformed line (skipped, logged), instead of throwing, which today ends the command as an internal error (exit 70).
Why: the coverage round of task 070 found the branch; the shipped script always writes string ids, but the client's
contract is "a bad line never takes the command down", and one branch breaks it.
Done when: a Core test feeds a reply with a numeric `id` and the command's next reply is still read, with a log line
naming the skipped one.
Part of the Exchange follow-ups bundle (081, 076) of the seventh round of 2026-10-30; the task session owns the
steps below and may reshape them.

## Docs that move

- `docs/development.md`, the sidecar protocol section: a malformed reply line, the `id` included, is skipped and logged

## Not in this change

- A change to the sidecar script's reply format
- The window's sidecar path beyond what the shared client gives it

## Steps

1. [x] `SidecarClient.TryReadReply` skips a reply line whose `id` is not a string; every skipped line (no JSON, no
       JSON object, no protocol version 1, no string id, the reply to another request) gets one verbose log line
       with its reason
2. [x] A Core test feeds a numeric-`id` reply followed by a good one and asserts the good one is read and the log
       names the skipped line
3. [x] The coverage gate stays at 100% (`scripts/check-coverage.py`)

## Log

- 2026-10-30 — promoted on `task/081-exchange-follow-ups` with task 076
- 2026-10-30 — `TryReadReply` split into `ReadReply`, which returns why a line is not the reply (or null for the
  reply), and the skip, which writes `a sidecar line skipped while waiting for the reply to request <id> (<reason>)`
  to `LogSink.Verbose` under the `exchange-online` category. A JSON object is named by its `id` and `type` (raw JSON,
  at most 40 characters each), never by its `data`, so a stale reply's objects never reach the log; any other line
  by its first 200 characters. The protocol version is read with `TryGetInt32`: `"aegis":1.5` threw a
  `FormatException` out of `GetInt32` the same way the numeric `id` threw out of `GetString`.
- 2026-10-30 — Core test `SidecarProtocolTests.AReplyWithANonStringIdIsSkippedAndLoggedAndTheNextReplyIsRead`: a
  scripted child answers the `invoke` with a numeric-`id` reply, an `aegis` 1.5 reply, a 309-character non-JSON
  line and a reply to another (50-character) id before the real reply; the real reply is read and the log names all
  four, the long values cut, and holds none of the skipped reply's data.

## Deviations from the plan

- Before this change no skipped line was logged at all (the step assumed "the log line the other malformed lines
  get"), so the log line is new and covers every kind of skipped line, not only the non-string id.
- The protocol version's `GetInt32` had the same hole (`"aegis":1.5` threw); it is fixed and tested with the id.

## Verification

- 2026-10-30 — in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes --no-restore`
  (clean), `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings), `dotnet test --no-build
  -c Release --coverlet …` (1269 tests, 0 failed), `scripts/check-coverage.py` (Core 5014/5014 lines, 2768/2768
  branches; command line 1894/1894 lines, 468/468 branches), `scripts/changelog.py --check` (up to date),
  `scripts/check-fixtures.py` (323 files, 0 findings). The sidecar script and its Pester tests are
  unchanged.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
