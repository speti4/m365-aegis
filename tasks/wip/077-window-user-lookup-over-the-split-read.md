---
size: S
origin: done/063-window-mailbox-access-over-the-layers.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 077 — The window's user lookup over the split read of task 043

## Context

The window's `mailbox-access` request for a user (which mailboxes a user can open or send as) runs the reverse
lookup the way the command line does since task 043, the FullAccess read split across several sidecars, instead of one
bulk read on the window's single warm session.
Why: a live user lookup in the window took 262 s on 2026-10-30 (one sidecar over every mailbox), while the command
line's split read takes 41 s over 620 mailboxes; four minutes is unusable for the support team.
Done when: the window's user lookup answers in the command line's time over the same tenant, the `window:` log line
names the number of sidecars, and the warm session for the mailbox direction is kept.
Part of the window follow-ups bundle (077, 080) of the seventh round of 2026-10-30; the task session owns the steps
below and may reshape them. The live timing over the tenant is the main session's (one signed-in round at a time);
the task session verifies over the fixtures and the fake module.

## Docs that move

- `docs/development.md`, "The window" and the mailbox-access section: the user direction runs the split read, the
  mailbox direction keeps the warm session, the `window:` line's sidecar count

## Not in this change

- A new bridge request or a front-end change beyond what the reply already carries
- The command line's split read itself (task 043)

## Steps

1. [ ] The bridge's `mailbox-access` user direction runs the split FullAccess read of task 043 (the same Core path
       the command line uses), the mailbox direction stays on the window's warm `ExchangeSession`
2. [ ] The `window:` log line of the user lookup names the number of sidecars
3. [ ] Core or Gui tests over the fake module: the user direction reaches the split read, the mailbox direction the
       warm session; the log line asserted
4. [ ] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/077-window-follow-ups` with task 080

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed; the live timing is the main session's after the merge>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
