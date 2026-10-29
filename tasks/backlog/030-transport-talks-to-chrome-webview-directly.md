---
status: idea
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
milestone: M4 Window
---

# The front end's transport talks to `chrome.webview` directly

What: with one host left (D-009), the `window.external` script the host injects is an extra layer between the
page and WebView2; the transport in `gui/web/src/bridge/transport.ts` posts and receives through
`chrome.webview.postMessage` and `chrome.webview.addEventListener('message', …)` instead, the host drops the
injected script, and the mock transport stays for the browser dev loop.
Why: one layer less to debug, and the message's origin and shape are WebView2's own.
Done when: the `--smoke` run and the bridge contract tests (C# and Vitest) pass without the injected script,
and `docs/development.md`, "The front end", describes the direct path. Can be folded into task 017.
