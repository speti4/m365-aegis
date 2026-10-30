---
size: S
origin: done/072-token-hygiene-test-across-logs-plans-and-output.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 080 — Token hygiene through the window: the bridge's sign-in and the `window:` log lines

## Context

The marker-token test of task 072 run through the window's path: the bridge's sign-in request, a lookup over the
Exchange token source, an error reply, and the `window:` log lines, with every file under the temp home, the bridge
replies and the window's stderr scanned for the marker.
Why: task 072 covers the command line and the shared Core sinks; the window writes its own `window:` lines and sends
replies over the bridge, which are sinks of their own.
Done when: the test is green over the bridge dispatcher with the fake token source, and injecting the marker into a
reply or a `window:` line on purpose makes it red.
Part of the window follow-ups bundle (077, 080) of the seventh round of 2026-10-30; the task session owns the steps
below and may reshape them.

## Docs that move

- `docs/development.md`, the safety tests section (task 072's entry): the window's path is covered too
- `SECURITY.md`, if it lists the token-hygiene test as a safeguard: the window named

## Not in this change

- The write-guard test over the bridge (task 079, waits for the first bridge write request)
- A change to what the `window:` lines carry beyond removing a leak the test finds

## Steps

1. [ ] A test over the bridge dispatcher with the fake token source of task 072: a `login`, a lookup over the
       Exchange token source, an error reply, and the `window:` lines, every sink scanned for the marker (the temp
       home's files, the replies, stderr)
2. [ ] A deliberate injection of the marker into a reply and into a `window:` line turns the test red (proven once,
       documented in the Log, not committed)
3. [ ] Any leak the test finds is fixed in the window or the dispatcher
4. [ ] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/077-window-follow-ups` with task 077

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
