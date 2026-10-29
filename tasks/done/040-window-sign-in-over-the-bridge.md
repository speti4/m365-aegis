---
size: M
origin: done/017-window-hello-bridge-behind-iuihost.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-30
milestone: M4 Window
---

# 040 — The window signs in: `login` over the bridge, the account picker parented to the window

## Context

A `login` request over the bridge (and `logout`), so the window's sign-in card offers "Sign in" when the
silent read of task 017 answers signed-out; the broker's account picker is parented to the window's handle
(the command line's `ConsoleWindow` becomes the host's handle through `IUiHost`), the device-code fallback is
shown in the card, and the card refreshes itself when the sign-in completes.
Why: a support colleague runs the window, not the command line (D-001); today the page can only show
signed-out and point at `m365-aegis login`.
Done when: a signed-out window signs in from the card on the maintainer's machine without a console, `whoami`
over the bridge answers signed-in afterwards, and the `--smoke` run still answers no request.
Part of the window M4 bundle (040, 045, 018) of the round of 2026-10-29; the task session owns the steps below
and may reshape them. The live check (the sign-in on the maintainer's machine) runs in the main session's round.

## Docs that move

- `docs/development.md`, "The front end" and the bridge: the `login` and `logout` requests, the handle through
  `IUiHost`, the device-code fallback in the card
- `README.md`: the window's sign-in under the bundle's own heading
- `tests/` bridge contract samples: the new requests and answers

## Not in this change

- the account's profile management from the window (`profile` stays on the command line)
- the task cards (task 018 sketches them)

## Steps

1. [x] `login` and `logout` over the bridge, the host's window handle through `IUiHost` to the broker, the
   device-code fallback surfaced as a bridge event or answer
2. [x] The sign-in card: "Sign in" when signed out, the code shown on the fallback, the card refreshes on completion
3. [x] Bridge contract samples, the Core and front-end tests; `--smoke` still answers no request
4. [x] Live check (main session): a signed-out window signs in on the maintainer's machine, `whoami` over the
   bridge answers signed-in
5. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the window M4 bundle (040, 045, 018)
- 2026-10-29 — API check against Microsoft Learn: MSAL.NET's "Acquiring tokens interactively" and the WAM page say a
  UI application passes its window through `WithParentActivityOrWindow` and must call `AcquireTokenInteractive`
  from the UI thread (`MsalError.WamUiThread`, "the call must be made from the UI thread", present in MSAL 4.91.1);
  WebView2's threading model says every `CoreWebView2` call (`PostWebMessageAsString` included) belongs to the UI
  thread. The handle already reached the broker through `IUiHost.WindowHandle` since task 017; the picker's call was
  not on the UI thread (the Core's awaits leave it), so `MsalTokenBroker.Create` gained an overload with an
  `onWindowThread` delegate: the client is built off the UI thread, only `AcquireTokenInteractive(...).ExecuteAsync`
  is started through the delegate, which the host implements as `IUiHost.InvokeOnUiThreadAsync` (`BeginInvoke` on
  the form, completing with the call). The command line keeps the old path (no delegate).
- 2026-10-29 — the bridge: `login` (`{"deviceCode": true}` or nothing) runs `AuthService.LoginAsync` for the window's
  profile (never writes one) and answers `signed-in`; the device code goes to the page as a `device-code`
  notification (no id) through a new `notify` callback of `BridgeDispatcher.DispatchAsync` and `IUiHost.Post`
  (marshalled to the UI thread); `logout` answers `logged-out` (`SignOutInfo`) and ends the window's Exchange
  session. The client hands id-less messages to `onNotification` handlers and gives a `login` 16 minutes (a device
  code lives about 15). Samples `login`, `device-code`, `logout`, `logged-out`; Core tests for the picker, the asked
  device code, the `window_handle_required` fallback, a cancelled picker, no profile, logout (6 new in
  `BridgeProtocolTests`).
- 2026-10-29 — the card: a state machine (`components/sign-in-state.ts`, 6 Vitest cases): "Sign in" and "Sign in with
  a code" when signed out with a profile, the code with a copy button while it waits (the window opens no other
  site, so the address is shown to type), the account when the reply comes, "Sign out" then a fresh `whoami`. Without
  a profile the card says the command line writes it once and shows its `login --tenant …` hint. The browser mock
  answers `login`, `logout` and the notification; `?mock=signed-out` starts it signed out. Looked at in the dev
  server with Playwright: signed out, the device code, signed in.

## Deviations from the plan

- `MsalTokenBroker.cs` (the one MSAL adapter) changed: the overload with the UI-thread delegate and
  `AcquireInteractiveAsync` split so only the picker's call starts on the window's thread; the console path is
  unchanged.
- The failure hints of `SignInFlow` still name the command line (`run m365-aegis login again`); the window shows them
  as they are (a follow-up in the pull request).

## Verification

- 2026-10-29 — offline, by the task session: `dotnet test -c Release` 521 green (24 new across 040 and 045), Vitest
  55 green; the browser mock with `?mock=signed-out` in Playwright: the card signed out with "Sign in" and "Sign in
  with a code", the code `MOCK1234` with its page and expiry while waiting, then signed in with "Sign out"; the
  Release window with an empty `M365AEGIS_HOME`: "There is no tenant profile yet" and no "Sign in" button, no MSAL
  call; `--smoke` exit 0 ("ready after 541 ms, window … (top-level)"); `doctor --offline` 0, `whoami` 3,
  `release.ps1 -DryRun` 0.
- 2026-10-29 23:37–23:52 (UTC+1) — live, in the main session's signed-in round on the maintainer's machine, this
  branch's Release window with `--verbose` against the real home: the card read signed in from the cache; "Sign
  out": MSAL's log shows the sign-out, the account removed from the cache and the broker, and the window's Exchange
  session ended with it (sidecar count 0); "Sign in": exactly one interactive broker call with its UI visible, the
  account picker on top of the window, no `wam_ui_thread`, no `window_handle_required`, no MSAL exception in the log;
  the card switched to signed in without a refresh, Refresh still answered signed in, and `m365-aegis whoami`
  afterwards answered with both tokens. The device code was not tried (one sign-in per round); it is covered by the
  Core tests and the browser mock.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after #21)
- [x] moved to `done/` with `closed: 2026-10-30`; the two follow-ups of the pull request filed as backlog 046
      (window-specific sign-in hints) and 047 (the "Open in browser" button)
