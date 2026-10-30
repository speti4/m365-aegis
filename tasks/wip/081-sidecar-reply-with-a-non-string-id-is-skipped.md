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

1. [ ] `SidecarClient.TryReadReply` skips a reply line whose `id` is not a string, with the log line the other
       malformed lines get
2. [ ] A Core test feeds a numeric-`id` reply followed by a good one and asserts the good one is read and the log
       names the skipped line
3. [ ] The coverage gate stays at 100% (`scripts/check-coverage.py`)

## Log

- 2026-10-30 — promoted on `task/081-exchange-follow-ups` with task 076

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
