---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M4 Window
---

# 017 — The window's first real page: the sign-in state and a lookup over the bridge, behind `IUiHost`

## Context

On the WinForms and WebView2 host (D-009, task 014), the front end gains its first real page: the sign-in
state from the core (`whoami` over the bridge), a group lookup (`group members`) with the table rendered in
shadcn/ui components, and the error path (an `error` reply shown as a toast); the bridge gains request and reply
types for these, the samples under `tests/contracts/bridge` grow with them, and both test suites read them.
Why: the window's first page that does work, and the proof that the bridge carries the core's answers.
Done when: the page shows the signed-in account and a group's members live, the `--smoke` run still passes, and
the C# and Vitest contract tests agree on the new messages.
Head of the Window bundle (017 with 030 folded in, 027, 028, 031, 029) of the round of 2026-10-29: task 030
(the transport talks to `chrome.webview` directly, the injected script dropped) is done inside this change
first. The window reuses the core's auth and Graph wiring the command line uses, no second code path. The task
session owns the steps below and may reshape them. The live check runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, "The front end" and the window: the direct transport, the page, the message types
- `README.md`: the window's first page
- `tests/contracts/bridge`: the new samples, and their README if one exists

## Not in this change

- task cards and the plan-and-confirm flow (task 018 and M4)
- a warm sidecar in the window (M4, after task 015)
- the verbose log switch (task 027)

## Steps

1. [x] Task 030 first: the transport posts through `chrome.webview.postMessage` and listens on
       `chrome.webview.addEventListener('message', …)`, the host drops the injected `window.external` script,
       the mock transport stays for `npm run dev`; `--smoke` passes
2. [x] The request and reply types for `whoami` and `group members` and the `error` reply on both sides
       (`Core/Bridge/BridgeProtocol.cs`, `gui/web/src/bridge/protocol.ts`), the samples, both contract tests
3. [x] The window holds the core's auth and Graph client the way the command line does; the host answers the
       two requests
4. [x] The page: the sign-in state, the group lookup with the table in shadcn/ui components, the toast
5. [x] Live check (main session): the signed-in account, a group's members, a bad group name as a toast
6. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Window bundle (017+030, 027, 028, 031, 029)
- 2026-10-29 — the protocol: `whoami` → `signed-in` (the core's `SignInInfo`) or `signed-out` (profile, account,
  message, hint; the `auth` failures of the silent sign-in, a missing profile included), `group-members` with
  `{"group"}` → `members` (the core's `GroupMembers`), `error` with the envelope's code, message and hint for any
  request (a failure of the core, `usage` for a missing group, `unknown-type`, `internal` with the stack trace in
  the log). Payloads are written with the envelope's JSON options (D-006). `BridgeMessageType` (C#) and
  `MessageType` (TypeScript) name the strings; six new samples under `tests/contracts/bridge` (`whoami`,
  `signed-in`, `signed-out`, `group-members`, `members`, `error`). The C# tests dispatch each request sample over
  the scripted broker and the Graph fixtures and compare the reply with its sample field by field; Vitest reads
  the replies through `readReply`, which checks each payload's shape.
- 2026-10-29 — the wiring: `BridgeDispatcher` became an instance over `BridgeServices` in the Core;
  `BridgeServices.ForWindow` builds the command line's `AuthService` over the same home folder (with the window
  handle as the broker's parent) and gets the Graph client from `GraphConnection` (a new overload taking the
  sign-in, so `M365AEGIS_GRAPH_FIXTURES` works for the window too). The internal-error failure and its log line
  moved from the Cli's `CommandRunner` to `Core/Diagnostics/InternalError.cs`; `CommandRunner.InternalFailure` and
  `LogInternalError` now delegate to it (no behavior change, its snapshot tests pass unchanged). `IUiHost.OnMessage`
  takes an async handler; the host awaits it and posts the reply back on the UI thread. `--smoke` answers no
  request, so a smoke run never reaches MSAL.
- 2026-10-29 — the page: `BridgeClient` (ids, the reply that repeats one, a 120 s timeout, an unreadable reply as
  an `error`), the sign-in card, the group members card with shadcn/ui's input, button, badge, card and table,
  and sonner's toast for every `error`. New packages `sonner` 2.0.8 and `next-themes` 0.4.6 (MIT, what shadcn's
  `sonner` component imports); `shadcn add table input card sonner badge` wrote the components. The dark
  `prefers-color-scheme` block of `index.css` got the tokens it lacked (popover, primary, secondary, muted,
  accent, input, ring), so the toast and the buttons follow the system's dark mode.
- 2026-10-29 — closed on `main` by the main session after the squash merge (#18). Of the pull request's three
  follow-ups the maintainer kept the window sign-in (backlog 040); `--profile` for the window is dropped (one
  tenant, D-001: the profile switch is a command-line convenience) and the `\u00XX` escapes of the bridge reply
  are dropped (valid JSON, cosmetic, nobody reads the raw reply).

## Deviations from the plan

- `InternalError` moved into the Core so the window reports an unexpected exception the way the command line
  does; `src/M365Aegis.Cli/Output/CommandRunner.cs` changed by two delegating lines, no command class and not
  `Program.cs`.
- The dark tokens in `index.css` were completed; without them a toast was white in dark mode.

## Verification

- 2026-10-29 — offline, by the task session: the Release window with an empty `M365AEGIS_HOME` and
  `M365AEGIS_GRAPH_FIXTURES` on the fixtures, driven over the WebView2 remote debugging port: the version from
  `pong`, `signed-out` with the core's "no tenant profile" message and the login hint, the `Finance` members table
  (4 rows), `Nothing` as a not-found toast with its hint; light and dark mode looked at with the browser mock.
  `--smoke` exit 0. C# `BridgeProtocolTests` and Vitest green over the same samples.
- 2026-10-29 ~22:03 (UTC+1) — live, in the main session's signed-in round (the silent sign-in from the cache),
  this branch's Release build driven over the WebView2 debugging port: the header shows `0.1.0`; the Sign-in card
  `signed in` with the display name and UPN, profile `default`, the tenant id, a Microsoft Graph and an Exchange
  Online token with their expiry; a synced security group looked up by its object id: `Security`, `synced`,
  13 direct members, 13 rows of type `group`, the same count as `m365-aegis group members <id>`;
  `zz-no-such-group-017`: one toast, "no group named 'zz-no-such-group-017'" with the hint, the earlier table kept;
  Refresh: the card back identical.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
