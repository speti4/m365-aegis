---
size: S
origin: done/071-write-guard-test-over-the-whole-command-tree.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 079 — A write-guard test over the window's bridge writes

## Context

A test that enumerates the bridge's write requests from the protocol itself (the way task 071 walks the command
tree) and drives each one without a confirmation, asserting that the fixture transport recorded no request other than
GET, the fake module no write cmdlet and the plans folder no record.
Why: SECURITY.md names a write without a confirmation as a vulnerability; task 071 holds it for the command line only,
and the window's plan-and-confirm cards (task 082) add the first write requests to the bridge.
Done when: the test is green over the bridge's write requests, a new write request joins it without an edit, and it is
part of task 082's done-when.
Held back on 2026-10-30 while the bridge had no write request; promoted with task 082 in the eighth round of
2026-10-30, on its branch. The task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The safety tests": the bridge write-guard test next to task 071's entry
- `SECURITY.md`: the safeguard named for the window, where the file names task 071's test

## Not in this change

- The write requests themselves (task 082)
- The command line's write-guard test (task 071)

## Steps

1. [ ] The write requests are recognizable from the protocol (a marker on the message type or a list the protocol
       owns), so the test enumerates them without an edit when one is added
2. [ ] The test drives each write request through the dispatcher without a confirmation and asserts the three sinks:
       the fixture transport (GET only), the fake module (no write cmdlet), the plans folder (no record)
3. [ ] A new request type that names a write but is missing from the enumeration fails the test (proven once, in the
       Log)
4. [ ] The docs under *Docs that move*; coverage stays at 100%

## Log

- 2026-10-30 — promoted on `task/082-window-mailbox-write-cards` with task 082

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
