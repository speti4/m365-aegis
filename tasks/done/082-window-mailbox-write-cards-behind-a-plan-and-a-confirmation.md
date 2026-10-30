---
size: L
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
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

1. [x] The bridge protocol: one plan request per write, named as the write's operation (`mailbox-access-grant`,
       `mailbox-access-revoke`, `mailbox-forwarding-set`, `mailbox-forwarding-clear`, marked `[BridgeWrite]`), answered
       with `plan` (the `Plan` as `--dry-run` prints it and the id the window holds it under); `apply-plan` with the
       plan's id and `"confirm": true`, answered with `plan-applied`; the write guard refuses an apply without the
       confirmation, for an unknown plan id, or older than 15 minutes
2. [x] The dispatcher runs the plan and the apply over the Core's mailbox writes of task 053 on the window's warm
       session, writes the plan record as the command line does (source `window`, the command line of the same plan),
       and logs a `window:` line per request with counts only
3. [x] The front end: the "Mailbox rights" card (grant, revoke, the rights, without automapping) and the "Mailbox
       forwarding" card (forward with or without a copy, stop forwarding), each ending in a plan view and a confirm or
       go back, following the sketch of task 018; going back sends nothing; the mock transport serves the new messages
4. [x] Task 079's write-guard test over the bridge's write requests, enumerated from the protocol, each driven without a
       confirmation: reads only in the fixture transport's log, no write cmdlet in the fake module's file, no record
5. [x] Tests: Core (the plan, the apply, the refusals, the record, the window lines), the bridge contract samples, a
       Cli resume of a window's record, the front end (Vitest over the cards, the flow, the mock and the samples), the
       token-hygiene test of task 080 extended with a write
6. [x] The docs under *Docs that move*; the coverage gate stays at 100% in every layer

## Log

- 2026-10-30 — promoted on `task/082-window-mailbox-write-cards` with task 079
- 2026-10-30 — the protocol (`Core/Bridge/BridgeProtocol.cs`): `BridgeWriteAttribute` marks the four write request
  types, `BridgeMessageType.Writes` lists the marked ones by reflection; `plan`, `apply-plan` and `plan-applied` beside
  them, and the payload records `MailboxAccessWriteRequest`, `MailboxForwardingWriteRequest`, `ApplyPlanRequest`,
  `PlanReady` (`planId`, `plan`) and `PlanApplied` (`plan`, `error`). Seven samples under `tests/contracts/bridge`:
  the four requests, `plan.json` (the grant sample's plan: FullAccess for Bob on Finance without automapping),
  `apply-plan.json` and `plan-applied.json` (the record named under `%LOCALAPPDATA%\m365-aegis`; the C# test puts the
  test's id and home in).
- 2026-10-30 — the dispatcher (`Core/Bridge/BridgeDispatcher.cs`): a write request makes the rows as the command does
  (`MailboxLookups.AccessRows`, `ForwardingRows`), checks a missing user, right or address and a "without automapping"
  without FullAccess in the card's words, plans over the window's `ExchangeBackends` and lets `PlanGuard.RunAsync`
  print it under `WriteMode.DryRun`; a plan with a change is held under a new 32-hex id with its `PlanStore`
  (`AuthService.Home`, new, gives the home; `BridgeServices.Clock`, new, the clock). `apply-plan` takes the plan out
  only with `"confirm": true` and applies it under `WriteMode.Apply`; the steps run on the `WindowExchange` the plan's
  backends carried, so the warm session sends them. The applied plan's change errors and the failure get the window's
  hints (`SignInHints.ForSurface`); the record keeps the core's. A sign-in and a sign-out drop every held plan; a new
  plan drops the ones older than `PlanLifetime`. `ForwardingRows` got an optional name for the address in its usage
  error ("the forwarding address"); the command line's message is unchanged.
- 2026-10-30 — the first run over the fake module (a probe, not committed): the grant sample planned, applied with
  the confirmation, the fake module's file `{"cmdlet":"Add-MailboxPermission","parameters":{"AccessRights":["FullAccess"],"AutoMapping":false,"Identity":"finance@contoso.com","InheritanceType":"All","User":"bob@contoso.com"}}`,
  the record `source: window`, `arguments: mailbox access grant finance@contoso.com --user bob@contoso.com --right
  FullAccess --no-automapping --yes`.
- 2026-10-30 — Core tests: `BridgeWriteTests.cs` (19 tests, 33 cases: the grant against `plan.json` and `plan-applied.json`, one
  sidecar, the record read back, a second apply refused and the grant visible to the next `mailbox-access`; the
  forwarding clear and set with their records; a revoke's rights in the given order; up-to-date without an id; the
  refused apply that keeps the plan; unknown ids; the lifetime and the purge; sign-in and sign-out; ten usage errors
  that start no sidecar; not found; no session; a partial apply (Send As for Bob and the mail contact) and a whole
  failure, each in the window's hints; a cancelled apply; the `window:` lines; the counts), `BridgeWindow.cs` (the
  window of the write tests), `BridgeProtocolTests` (the sample list), `BridgeTokenHygieneTests` (a grant planned and
  applied in the marked run: the plan record under `plans\` is searched too, five `window: mailbox-` lines and one
  `window: apply-plan`). `SidecarFixtures` takes the fake module's write file. Cli: `PlanCommandTests.AResumeContinuesARecordTheWindowWrote`
  writes a record with the window's source and arguments cut short while applying and resumes it with `--dry-run`.
- 2026-10-30 — the front end: `bridge/protocol.ts` (the messages, `writeRequests`, the `Plan` types and their
  checks), `bridge/mock-writes.ts` (the mock plans the writes over its holders and forwarding and holds them as the
  host does; its apply changes nothing and fails a Send on Behalf change on Reception, to show a partial result),
  `components/write-flow.ts` (the card's flow as a reducer: pick, planning, plan or refused, confirming, applying,
  result), `components/plan-view.tsx` (the steps, the plan, the confirmation, the result, after the sketch's parts),
  `components/mailbox-write-cards.tsx` (`WriteCard` and the two cards), both cards on `App.tsx` under the access card.
  A refusal shows "Refused: nothing was changed", an apply that broke off otherwise "Stopped: check the mailbox before
  you try again". The sketch keeps its flow; its note and comment now point at the real cards.
- 2026-10-30 — the browser check: `npm run dev` on a port of its own, driven with `playwright-cli`: the rights card
  with Finance and Bob, the plan, the confirmation (the button disabled until the box is ticked), "Done" with the
  record's line; the screenshots stayed in the scratchpad.
- 2026-10-30 — docs: `docs/development.md` ("Writes and plans": the record's `window` source and a new "The window's
  writes"; "The front end": the write requests among the messages and "The write cards"; the sketch's paragraph; "The
  safety tests": task 080's run with the write, task 079's test), `README.md` (the window's section, the safety tests'
  line), `SECURITY.md` (the bridge messages among the token sinks, the window's confirmation in the write rule).
- 2026-10-30 — merged in #108 (squash); both follow-ups approved and filed: task 090 (the plan warnings in the window's
  own words) and task 091 (the access card refreshes after an apply); closed on `main` by the main session. The live
  grant and revoke from the window over the tenant are still the main session's signed-in round.

## Deviations from the plan

- One shape per write instead of a generic plan request: each request is named as the write's `operation`, and the
  one `apply-plan` applies any held plan.
- Added beyond the steps: a plan's lifetime (15 minutes; an older one is refused, the card plans again) and the drop of
  held plans at a sign-in and a sign-out, so a plan is never applied under another account or long after it was read.
- The record's `arguments` are the command line of the same plan with `--yes`, not empty as `PlanRecordContext`
  first foresaw for the window, so `plan show` reads it and the command line's `--resume` continues it (a Cli test
  holds that); a resume from the window stays out of scope.
- The browser mock applies nothing: its reads stay the same after a write, so the access card does not show a grant
  the mock "applied".
- The sketch of task 018 is followed, not rebuilt: the cards reuse its steps, plan view, checkbox confirmation and
  result; its code stays dev-only, and only its note and comment changed.
- The colleague's review of the sketch (task 018, moved to the first card task) did not happen in this session: it
  is the maintainer's, now over the real cards.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (1319 tests, 0 failed, 0 skipped), the coverage gate (`M365Aegis.Core.dll`
  5171/5171 lines and 2836/2836 branches, `m365-aegis.dll` 1894/1894 and 468/468), `python -I scripts/changelog.py
  --check`, `python -I scripts/check-fixtures.py` (330 files, 0 findings); the front end: `npm ci`, `npm run lint`,
  `npm run typecheck`, `npm run test:coverage` (151 tests, 100% statements, branches, functions and lines), `npm run
  build` (no sketch string in `dist/`). No package was added.
- 2026-10-30 — over the fake module: a FullAccess grant and a forwarding clear from the bridge with a plan, a
  confirmation and a plan record (`BridgeWriteTests`); every refused apply and the cards' going back send nothing
  (`BridgeWriteGuardTests`, the card tests). The live grant and revoke on a test mailbox from the window are the main
  session's, after the merge.
- 2026-10-31 — live, after the merge, on a technical mailbox of the tenant with the maintainer's everyday account as the
  holder, from the window under `--verbose`: a FullAccess grant without automapping (plan 1.7 s, apply 1.8 s), the
  revoke (plan 1.7 s, apply 0.8 s), a forwarding set with a copy kept (plan 0.5 s, apply 0.5 s) and its clear (plan
  0.2 s, apply 0.4 s), every one on the warm session (`sidecar starts 1` throughout) with two `window:` lines of counts
  only. Each record is `version 2`, `source: window`, carries the equivalent command line with `--yes`, and `plan show`
  reads it with the window's start and finish; the command line's `mailbox access` showed the grant after the apply
  and no grant after the revoke, and `mailbox forwarders` did not list the mailbox after the clear. A "Back to the
  plan" before the revoke's confirmation sent nothing: no request, no record. The Send As and Send on Behalf paths
  were not exercised live.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-30` by the main session after the merge
