---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 082 — The window's mailbox write cards: access grant and revoke, forwarding set and clear, behind a plan and a confirmation

## Context

The bridge's first write requests and the task cards over them: `mailbox access grant` and `revoke` (FullAccess,
Send As, Send on Behalf, `--no-automapping`), `mailbox forwarding set` and `clear`; each card ends in the `Plan` the
command line prints and a confirmation that stands for `--yes`, the plan record is written as the command line writes
it, and the write runs over the window's warm `ExchangeSession`.
Why: the M4 done-when is a support colleague granting FullAccess on a shared mailbox from the window without
PowerShell; the writes exist behind the command line since task 053, the window has only reads (task 063).
Done when: a FullAccess grant and a forwarding clear run from the window over the fake module with a plan card, a
confirmation and a plan record; a cancelled card sends nothing; the write-guard test of task 079 is green over every
bridge write request (079 is part of this done-when); the sketch of task 018 is followed or amended; `SECURITY.md` names
the bridge messages among the sinks (task 080's follow-up).
Decided 2026-10-30: the whole mailbox write set in one task, not the FullAccess grant alone.
Eighth round of 2026-10-30, on its own branch with task 079; the task session owns the steps below and may reshape
them. The live run over the tenant (a grant and a revoke on a test mailbox from the window) is the main session's.

## Docs that move

- `docs/development.md`, "The front end" and "Writes and plans": the write requests of the bridge (plan, confirm, the
  record), the cards, how a confirmation stands for `--yes`
- `README.md`, the window section: the write cards
- `SECURITY.md`, "Sign-in and tokens": the window's bridge messages named among the sinks; the write guard's rule
  names the window's confirmation next to `--yes`
- `tests/contracts/bridge/`: the new request and reply samples (the front end's tests read them)

## Not in this change

- Group writes (members, owners) and the CSV bulk writes in the window
- A resume of a half-applied plan from the window (`plan list`, `plan show`, `--resume` stay command-line only)
- A pool of warm sidecars for the window

## Steps

1. [ ] The bridge protocol: a plan request per write (`mailbox-access-plan`, `mailbox-forwarding-plan`, or one
       shape the design decides), answered with the `Plan` as the command line renders it; an apply request that
       carries the plan's id and the confirmation, answered with the apply's result; the write guard refuses an apply
       without a confirmation or for an unknown plan id
2. [ ] The dispatcher runs the plan and the apply over the Core's existing mailbox writes (task 053) on the window's
       warm session, writes the plan record as the command line does, and logs a `window:` line per request without
       the token or the targets' secrets
3. [ ] The front end: the access card (grant, revoke, the rights, `--no-automapping`) and the forwarding card (set with
       `--keep-copy`, clear), each ending in a plan view and a confirm or cancel, following the sketch of task 018
       (`gui/web/src/sketch`); a cancel sends nothing; the mock transport serves the new messages
4. [ ] Task 079's write-guard test over the bridge's write requests, enumerated from the protocol (every request
       type that names a write), each driven without a confirmation: the fixture transport saw only GET, the fake
       module no write cmdlet, the plans folder no record
5. [ ] Tests: Core (dispatcher plan, apply, refusal, the record), the bridge contract samples, the front end (Vitest
       over the cards and the samples), the token-hygiene test of task 080 extended with a write request
6. [ ] The docs under *Docs that move*; the coverage gate stays at 100% in every layer

## Log

- 2026-10-30 — promoted on `task/082-window-mailbox-write-cards` with task 079

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed; the live grant and revoke are the main session's after the merge>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
