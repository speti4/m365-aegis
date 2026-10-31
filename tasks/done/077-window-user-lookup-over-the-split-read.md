---
size: S
origin: done/063-window-mailbox-access-over-the-layers.md
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M4 Window
---

# 077 — The window's user lookup over the split read of task 043

## Context

The window's `mailbox-access` request for a user (which mailboxes a user can open or send as) runs the reverse
lookup the way the command line does since task 043, the FullAccess read split across several sidecars, instead of one
bulk read on the window's single warm session.
Why: a live user lookup in the window took 262 s on 2026-10-30 (one sidecar over every mailbox), while the command
line's split read takes 41 s over some 600 mailboxes; four minutes is unusable for the support team.
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

1. [x] The bridge's `mailbox-access` user direction runs the split FullAccess read of task 043 (the same Core path
       the command line uses), the mailbox direction stays on the window's warm `ExchangeSession`
2. [x] The `window:` log line of the user lookup names the number of sidecars
3. [x] Core or Gui tests over the fake module: the user direction reaches the split read, the mailbox direction the
       warm session; the log line asserted
4. [x] The coverage gate stays at 100% in every layer touched

## Log

- 2026-10-30 — promoted on `task/077-window-follow-ups` with task 080
- 2026-10-30 — why the window was slow: `WindowExchange.Backends` made its `ExchangeBackends` without a way to start
  more sidecars, and the dispatcher called the sidecar-only `MailboxLookups.AccessForUserAsync(backends.Sidecar, …)`,
  whose options are `piped`: one bulk `Get-EXOMailboxPermission` over every mailbox on the warm session.
- 2026-10-30 — the change: `WindowExchange.Backends` passes `newSidecar` to `ExchangeBackends`, a session made with the
  holder's own factory over the window's token source (`_create(CurrentTokens())`), so the extra sidecars connect with
  the window's one token and end with the request's `ExchangeBackends`; the warm session is untouched. The dispatcher's
  user direction runs `MailboxLookups.AccessForUserAsync(backends, user, new UserAccessOptions(), ct)`, the command
  line's path with its defaults (`parallel`, 4 sidecars, no group expansion): the first share on the warm session, the
  others on the started sidecars. The mailbox direction is unchanged (`AccessAsync` never splits, so it starts none).
- 2026-10-30 — the log line: `ExchangeBackends.SidecarsStarted` counts what `StartSidecar` started (the count stays
  after the dispose), and `BridgeDispatcher.DescribeSidecars` writes it into the request's `window:` line by counts
  only: `mailbox-access answered in … ms through pwsh (FullAccess of 6 mailbox(es) read parallel over 3 sidecar(s): the
  window's session and 2 started for this request); exchange session 1, sidecar starts 1, …` over the fake module's
  six mailboxes (shares of `ceil(6/4) = 2`, so three sidecars; over some 600 mailboxes it is four).
- 2026-10-30 — `BridgeServices.ForWindow` keeps one `ExchangeSessionOptions` per token source (a
  `ConditionalWeakTable`), as the command line's `ExchangeRunner` shares one options object between its session and the
  extra ones: the options memoize the target, so the extra sidecars ask Graph for the organization's name once per
  sign-in, not once each.
- 2026-10-30 — tests (`tests/M365Aegis.Core.Tests/BridgeEdgeTests.cs`): over the fake module a mailbox lookup makes one
  session, then a user lookup makes two more, which have started once and ended (`Client` null) after the request,
  while the warm one is the holder's `Session` still, connected once and alive; the `window:` line asserted word for
  word in its sidecar part; over the layered holder the three sessions ask the window's token source once
  (`Acquisitions` 1); `DescribeSidecars` holds counts only, no name. `BridgeProtocolTests.MailboxAccessOfAUserIsTheUserGrantsSample`
  holds the reply to the updated sample.
- 2026-10-30 — merged in #105 (squash). The follow-up "keep the window's extra sidecars warm across user lookups"
  was not filed: it is worth a task only if the live timing shows the three cold sidecar starts dominate, and that
  measurement decides it (the maintainer's call of 2026-10-30). Closed on `main` by the main session.

## Deviations from the plan

- The `user-grants` reply now carries `"method": "parallel"` (the layered read names its method, as `mailbox access
  --user --json` does); `tests/contracts/bridge/user-grants.json` has the field. The front end's reader ignores it, so
  `gui/web` is unchanged; its whole check list ran because its Vitest tests read the sample.
- One new public member in the Core, `ExchangeBackends.SidecarsStarted`, and the `ForWindow` options sharing above;
  neither was named in the Steps.

## Verification

- 2026-10-30 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true` (0 warnings),
  `dotnet test --no-build -c Release` (1267 tests, 0 failed), the coverage run and `python -I scripts/check-coverage.py
  TestResults/coverage` (`M365Aegis.Core.dll` lines 5005/5005, branches 2754/2754; `m365-aegis.dll` lines 1894/1894,
  branches 466/466), `python -I scripts/changelog.py --check`, `python -I scripts/check-fixtures.py` (323 files, 0
  findings); the front end because of the sample: `npm ci`, `npm run lint`, `npm run typecheck`, `npm run
  test:coverage` (117 tests, 100% statements, branches, functions and lines), `npm run build`.
- Not verified here: the live timing of a user lookup in the window over the tenant (the target: the command line's
  41 s over some 600 mailboxes, against 262 s before) and the live `window:` line with `over 4 sidecar(s)`. Both are the main
  session's, in its signed-in round after the merge.
- 2026-10-31 — live, after the merge, over the tenant's some 600 mailboxes: the window's user lookup answered in 54.0 s
  (53 793 ms; 262 s on 2026-10-30 before this task, the command line's split read 41 s), and the `window:` line read
  `FullAccess of N mailbox(es) read parallel over 4 sidecar(s): the window's session and 3 started for this
  request`. A mailbox lookup right after answered in 1.4 s through `admin-api+pwsh` with `sidecar starts 1`
  unchanged, so the warm session served it and no sidecar was started. The 13 s over the command line is the three
  cold sidecar starts, the measure the warm-pool follow-up was waiting for.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-30` by the main session after the merge
