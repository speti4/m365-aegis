---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 013 — Spike: the Windows account manager from a console process, and where device code has to take over

## Context

D-003 rests on the broker (the Windows account manager) working from a console process; the research of
2026-10-28 marked it as the one auth spike before task 002. A timeboxed try (one day) of the broker in the
command line: the parent window from `GetAncestor(GetConsoleWindow(), GetRootOwner)` through
`WithParentActivityOrWindow`, in Windows Terminal, conhost, the VS Code terminal and with stdout redirected (the
tests' case); the silent `OperatingSystemAccount` path on the Entra-joined machine; the behavior without a
window (an SSH session, a scheduled task), where the code falls back to device code; and whether the tenant's
Conditional Access policies block device code. The result shapes the fallback order of task 002 and a note in
D-003. Done when a dated note lists each environment with the result, and task 002's fallback order is written
from it. This session holds the signed-in round (one at a time across sessions); the app registration and the
tenant come from the maintainer's local instructions and never enter the repo.

## Docs that move

- `tasks/decisions/D-003-own-public-client-with-msal-and-wam.md`: a dated note with the broker's behavior from a
  console and the fallback order it implies.
- `tasks/backlog/002-auth-with-msal-and-the-windows-account-manager.md`: the fallback order rewritten from the
  findings.
- Both are other task files, which a task session never edits (`docs/maintainer-sessions.md`): the findings
  land in this file's Log, and the main session writes the two edits when it prepares the pull request.

## Not in this change

- The `Auth` component itself, `login`, `logout`, `whoami`, the token cache (task 002).
- The Exchange token hand-off to the sidecar (task 009) and the Admin API (task 010).
- Any change to the app registration beyond what the spike needs to sign in.

## Steps

1. [x] A throwaway console program (outside the repo, or under `tests/` as a disposable spike project not shipped) with
       MSAL.NET and the broker package, signing in silently and interactively against the tenant
2. [x] Windows Terminal, conhost, the VS Code terminal, stdout redirected: result and timing of each
3. [x] No window (an SSH session or a scheduled task): where the broker fails and device code takes over; the
       Conditional Access verdict on device code
4. [x] The dated note in this task's Log and in D-003; task 002's fallback order rewritten
5. [x] Verification: no token, tenant id or user principal name in any committed file (`check-fixtures.py`, a grep)

## Log

- 2026-10-29 — promoted by the main session; the work runs in the worktree on this branch
- 2026-10-29 — the app registration of D-003 was created in the tenant in this session (single tenant, "Mobile
  and desktop" platform with the broker redirect and `http://localhost`, public client flows allowed; delegated
  `User.Read`, `Exchange.Manage` and `Exchange.ManageV2` with admin consent; no credential). `Exchange.ManageV2`
  sits in the "Office 365 Exchange Online" API next to `Exchange.Manage` and is grantable to a public client,
  which answers half of the research's open question for task 010. The ids went to the maintainer's local
  instructions only.
- 2026-10-29 — the spike: a throwaway console program outside the repo (MSAL.NET 4.91.1 with the broker
  package, the versions `Directory.Packages.props` pins; `net10.0-windows10.0.17763.0`; the parent window from
  `GetAncestor(GetConsoleWindow(), GA_ROOTOWNER)`; `BrokerOptions` with `ListOperatingSystemAccounts`; the scope
  `User.Read`). Every run went over a Remote Desktop session on the maintainer's machine, which is **not
  Entra-joined**: `dsregcmd` reports workplace-registered to the tenant, no primary refresh token. Results:

  | Environment | Step | Result |
  |---|---|---|
  | VS Code terminal, Windows Terminal, conhost, stdout redirected | silent, `OperatingSystemAccount` | fails in 0.1–0.5 s with `failed_to_acquire_token_silently_from_broker` (WAM error `0xCAA10001`): no primary refresh token on a registered-only machine |
  | VS Code terminal | silent, an account the broker lists | OK in 0.9 s, token source Broker, no UI |
  | stdout, stdin and stderr redirected, no console window (hwnd 0) | silent, an account the broker lists | OK in 0.3 s, token source Broker, no UI |
  | Windows Terminal (`WT_SESSION` set) | interactive, broker | OK in 6.3 s including the click: the account picker on top of the terminal's root owner window |
  | conhost (`Win+R`, `powershell`) | interactive, broker | OK in 5.5 s including the click, the picker on the root owner window |
  | No console window (hwnd 0) | interactive, broker | fails in 0.3 s with `MsalClientException` `window_handle_required`, no UI |
  | Windows Terminal | device code, no broker | OK in 57 s including the browser round; the tenant's Conditional Access lets device code through |

  The broker lists three accounts on this machine, two of them in the tenant: the maintainer's everyday
  account first and the admin account later. `GetAccountsAsync()[0]` therefore returned the everyday account
  while the picker returned the admin one. An SSH server is not installed on the machine, so the hwnd-0 run
  stands in for the SSH and scheduled-task cases: the broker's refusal is immediate and silent, and the code
  can fall back to device code without a timeout. Every token carried every consented scope, with
  `User.Read` the only one requested.
- 2026-10-29 — the note in D-003 and the fallback order in task 002 were written on this branch by the main
  session, which ran the spike itself (the maintainer had to be present for the sign-ins).

## Deviations from the plan

- The spike ran on a workplace-registered machine over Remote Desktop, not on an Entra-joined one: the silent
  `OperatingSystemAccount` path could not be measured on a machine with a primary refresh token. Task 002 keeps
  that path first in the order and treats its failure as the normal case on a registered machine.
- The main session edited D-003 and task 002 on this branch instead of after the pull request, since it was the
  session that ran the spike.

## Verification

- 2026-10-29 — the seven measurements above, each from the spike's own output; `python -I
  scripts/check-fixtures.py` green; a grep over the tree for the client id, the tenant id, the tenant's name and
  the two user principal names finds nothing outside the gitignored `CLAUDE.local.md`; `tasks_check` 0 errors
  (the stale-index report is the main session's to clear); `python -I scripts/changelog.py --check` green after
  the regeneration

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session files the approved ones
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: YYYY-MM-DD`
