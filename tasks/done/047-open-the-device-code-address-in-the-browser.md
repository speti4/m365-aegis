---
size: S
origin: done/040-window-sign-in-over-the-bridge.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M4 Window
---

# 047 — An "Open in browser" button for the device-code sign-in address

## Context

The device-code card shows the sign-in address as text to type, because the window opens no other origin; an
"Open in browser" button hands the address to the system browser through the host (`IUiHost`, a `ShellExecute` of
the URL only, never a navigation of the WebView2), next to the copy button of the code.
Why: typing the address is the slow step of the fallback, and the window must stay closed to other origins.
Done when: the button opens the address in the default browser on the maintainer's machine, the window itself
still refuses other origins, and the browser mock covers the button.
Part of the Exchange and window follow-ups bundle (050, 049, 048, 046, 047) of the round of 2026-10-30; the task
session owns the steps below and may reshape them. The host opens only the address the device-code answer carried
(an `https` URL on a Microsoft sign-in host), never an arbitrary string from the front end.

## Docs that move

- `docs/development.md`, "The front end": the host's open-in-browser member and its one allowed use

## Not in this change

- any navigation of the WebView2 to another origin
- a browser-based sign-in replacing the broker (D-003)

## Steps

1. [x] The host member (`IUiHost`) that opens a URL in the default browser, the allowed-host check, the bridge message
2. [x] The button on the device-code card, next to the code's copy button; the mock transport covers it
3. [x] Core or Gui tests for the host check; front-end tests; the bridge contract sample for the new message
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window follow-ups bundle (050, 049, 048, 046, 047)
- 2026-10-30 — the request `open-sign-in-page` has no payload: the dispatcher remembers the page of the device-code
  prompt it sent while that `login` runs (and forgets it when the login ends), checks it with `Bridge/SignInPage.cs`
  (absolute, https, the default port, no user info, the host `microsoft.com`, `www.microsoft.com`,
  `login.microsoftonline.com` or `login.microsoft.com`, as the identity platform's device code flow names its page)
  and hands it to `BridgeServices.OpenInBrowser`, answered with `sign-in-page-opened`. No sign-in waiting is the
  `usage` error, a page off the hosts the `service-error` one, and nothing is opened in either case.
- 2026-10-30 — `IUiHost.OpenInBrowser` in the WinForms host checks the page again and starts it with
  `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`; a browser that does not start is a
  `service-error` with the address to type as the hint. The WebView2's navigation guard and its refusal of new
  windows are unchanged. The device-code card's "Open in browser" sits next to the page's address; the browser mock
  answers the request while its device-code sign-in waits.

- 2026-10-30 — closed by the main session on `main` after #23; the approved follow-ups of the pull request are filed as backlog items 055–062

## Deviations from the plan

- the button sits by the page's address rather than by the code's copy button: it opens the address, the copy button
  copies the code

## Verification

- 2026-10-30 — Core tests (`BridgeSignInPageTests`): the allow-list over fourteen addresses (the four hosts, case
  folded, against http, another port, user info, look-alike hosts, `aka.ms`, `file:` and `javascript:`); while a
  device-code sign-in waits, `open-sign-in-page` gives the `sign-in-page-opened` sample and the browser gets that one
  page; before and after the sign-in it is `usage` and nothing opens; a prompt on `login.contoso.com` is never opened;
  a window without a browser says so. Vitest: the samples read and written, the mock answers while its sign-in waits
  and refuses otherwise. `dotnet test` 645 passed; Vitest 57 passed.
- 2026-10-30, live (the window from this branch, a temporary home whose profile names an account the broker does not
  hold, the maintainer in front of it): "Sign in with a code" showed the device-code card with "Open in browser"; the
  click opened the default browser on the device-login page while the window stayed on its own page (MSAL logged
  `Token Acquisition (DeviceCode) started`). The code was not entered; the temporary home is deleted.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
