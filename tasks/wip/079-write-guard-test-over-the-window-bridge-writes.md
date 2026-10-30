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

1. [x] The write requests are recognizable from the protocol: the `[BridgeWrite]` mark on a `BridgeMessageType`
       constant, and `BridgeMessageType.Writes` lists the marked types by reflection, so the test enumerates them
       without an edit when one is added
2. [x] The test drives each write request through the dispatcher without a confirmation and asserts the three sinks:
       the fixture transport's request log (reads only), the fake module's write file (empty), the plans folder (no
       record)
3. [x] A new request type that names a write but is missing from the enumeration fails the test (proven once, in the
       Log)
4. [x] The docs under *Docs that move*; coverage stays at 100%

## Log

- 2026-10-30 — promoted on `task/082-window-mailbox-write-cards` with task 082
- 2026-10-30 — `tests/M365Aegis.Core.Tests/BridgeWriteGuardTests.cs` over `BridgeWindow` (the window of task 082's
  tests: a temporary home with a profile, the recorded Graph and Admin API answers through fixture transports that
  write each request to one log, the window's layers over a made-up token source with the session on the fake module,
  which writes each write cmdlet to a file, a clock that stands still until a test moves it).
  `AWriteRequestWithoutAConfirmationSendsNothing` is a theory over `BridgeMessageType.Writes` (four today): the write is
  planned from its sample under `tests/contracts/bridge` (a `plan` reply, `dry-run`, at least one change, an id), planned
  again, then nine applies that are not a confirmed apply of a held plan (no `confirm`, `false`, `"true"`, `1`, no id, the
  id upper-cased, another id, no payload, other field names) must each answer `error`, the sinks are checked, and then a
  plan older than its lifetime, a plan a `logout` dropped and the second plan after the sign-out are refused too, and the
  sinks checked again. `NoMessageOfTheProtocolWritesWithoutAConfirmation` sends every message type of the protocol from
  its sample: a type answered with `plan` must carry the mark, and nothing may reach a layer.
  `EveryMessageTypeHasItsSampleNamedAfterIt` holds every constant of `BridgeMessageType` to a sample `<type>.json`, so
  a new type cannot skip the sweep; `TheProtocolMarksWriteRequests` holds the list non-empty and inside the protocol.
- 2026-10-30 — the request log: task 071's reader (a GET, a `$batch` of GETs and an Admin API call of a `Get-` cmdlet
  read) moved from `WriteGuardTests` to `tests/shared/SentRequests.cs`, linked into both test projects; the command
  line's guard test calls it unchanged.
- 2026-10-30 — red proof 1 (not committed): the `[BridgeWrite]` mark taken off `MailboxForwardingClear`, as a write
  request missing from the enumeration: `NoMessageOfTheProtocolWritesWithoutAConfirmation` failed with
  `'mailbox-forwarding-clear' answers with a plan but is not marked [BridgeWrite]` (1 of 6). Red proof 2 (not
  committed): `ApplyPlanAsync` running the guard's apply before it answers the refusal of an unconfirmed apply, a
  hidden write behind a correct reply: the four theory cases failed on the sinks, `a write reached a layer without a
  confirmation: fake module {"cmdlet":"Add-MailboxPermission",…}` (and `Set-Mailbox` twice, `Remove-RecipientPermission`).
  Both restored from a copy; the class green again (7 tests).
- 2026-10-30 — docs: `docs/development.md`, "The safety tests", the window's write guard after task 071's paragraph.

## Deviations from the plan

- "The fixture transport saw only GET" is read as task 071 reads it: a `$batch` of GETs and an Admin API call of a
  `Get-` cmdlet are reads (every Admin API call is a POST).
- A new write request joins the theory with its mark and its sample; the sample is not a test edit but the
  contract's own rule, which `EveryMessageTypeHasItsSampleNamedAfterIt` now holds for every type.
- `SECURITY.md` names no test (task 080's deviation), so it gained no safeguard line here; task 082 names the
  window's confirmation in its write rule and the bridge messages among the token sinks.

## Verification

- 2026-10-30 — `BridgeWriteGuardTests` green (7 tests), red under each proof above. The local matrix of task 082
  (below in its file), all exit 0: 1319 .NET tests, the coverage gate at 100% lines and branches for
  `M365Aegis.Core.dll` and `m365-aegis.dll`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (`SECURITY.md` names no test, see the deviation)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
