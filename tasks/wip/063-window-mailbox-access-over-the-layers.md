---
size: S
origin: done/056-window-mailbox-list-through-the-admin-api.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 063 — The window's `mailbox-access` over the layers

## Context

The window's `mailbox-access` request answers the way the command line's `mailbox access` does since task 042:
the mailbox and its Send on Behalf delegates with their display names from the Admin API, FullAccess and Send As
from the warm sidecar session, side by side over the one Exchange token the window holds since task 056.
Why: parity with the command line; today the window's card reads everything through the sidecar and shows the
delegates without their names.
Done when: the window's access card shows the same rows as `mailbox access <mailbox>`, the `window:` log line
names both layers, and the bridge tests hold it over the fixtures and the fake module.
The one task of the window bundle of the sixth round of 2026-10-30; the task session owns the steps below and
may reshape them. The writes follow-ups bundle (064–066) runs in parallel on `GroupWrites`, `PlanResume` and
`PlanStore`, and the safety tests bundle (071–075) on the tests; this task stays in `Core/Bridge`,
`Core/Exchange` reads it reuses and `gui/web`. The live check is the main session's: it starts the window from
the worktree's build, the maintainer opens the access card, the `window:` lines and the card's rows are compared
with `mailbox access <mailbox>` (counts and shapes only).

## Docs that move

- `docs/development.md`, the window and bridge section: the `mailbox-access` request's layers
- `README.md`: the bundle's own heading
- `tests/contracts/bridge`: the request and reply samples, read by the C# and the Vitest tests

## Not in this change

- A write from the access card (M4's plan-and-confirm card is its own task)
- The window's `mailbox-list` (task 056) and the hints (task 057)

## Steps

1. [ ] The bridge handler (`WindowExchange`, `BridgeDispatcher`) runs the two layers the way `MailboxLookups` does
       for the command line, over the window's Exchange token source; the `window:` log line names both layers
2. [ ] The front end's access card shows the rows with the display names and the layer; the contract samples
3. [ ] Bridge tests (C# over the fixtures and the fake module; Vitest over the samples)
4. [ ] Docs; the live steps for the main session in the report

## Log

- 2026-10-30 — promoted by the main session as the window bundle of the sixth round

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
