---
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M4 Window
---

# 030 — The front end's transport talks to `chrome.webview` directly

## Context

With one host left (D-009), the `window.external` script the host injects is an extra layer between the
page and WebView2; the transport in `gui/web/src/bridge/transport.ts` posts and receives through
`chrome.webview.postMessage` and `chrome.webview.addEventListener('message', …)` instead, the host drops the
injected script, and the mock transport stays for the browser dev loop.
Why: one layer less to debug, and the message's origin and shape are WebView2's own.
Done when: the `--smoke` run and the bridge contract tests (C# and Vitest) pass without the injected script,
and `docs/development.md`, "The front end", describes the direct path.
Folded into task 017 in the Window bundle of the round of 2026-10-29: the work is the first step of 017, this
file records its own Verification.

## Docs that move

- `docs/development.md`, "The front end": the direct path (with task 017)

## Not in this change

- the page itself (task 017)

## Steps

1. [x] Done as step 1 of task 017: the direct transport, the injected script dropped, the mock transport kept
2. [x] `--smoke` and both contract test suites pass without the injected script

## Log

- 2026-10-29 — promoted by the main session into the Window bundle, folded into 017
- 2026-10-29 — `WebViewTransport` posts the message's JSON text with `chrome.webview.postMessage` and listens on
  `chrome.webview`'s `message` event (string data only, anything else dropped); `findWebView` looks for
  `chrome.webview` (plain Chrome has `chrome` without it), the mock answers otherwise. `onMessage` returns its
  unsubscribe. The host's `BridgeScript` and its `AddScriptToExecuteOnDocumentCreatedAsync` are gone.

## Deviations from the plan

- none

## Verification

- 2026-10-29 — `m365-aegis-gui --smoke` from the Release build with an empty `M365AEGIS_HOME`: "smoke: ready after
  695 ms, window 0x… (top-level)", exit 0, no injected script.
- 2026-10-29 — the hosted path end to end in the real window (empty home, `M365AEGIS_GRAPH_FIXTURES` on the
  fixtures, the page driven over the WebView2 remote debugging port): `pong` with the version, `signed-out` from
  the core, the `members` table for `Finance`, the not-found `error` as a toast.
- 2026-10-29 — Vitest: `transport.test.ts` (the transport over a fake `chrome.webview`: the JSON text posted,
  only string replies parsed, the unsubscribe; the mock) and `protocol.test.ts` over the samples; C#
  `BridgeProtocolTests` over the same samples; both green.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
