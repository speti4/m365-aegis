---
status: idea
size: S
origin: done/049-one-exchange-token-source-for-the-sidecar-and-the-admin-api.md
created: 2026-10-30
milestone: M4 Window
---

# The window's `mailbox-list` through the Admin API over the shared token source

What: the window holds only its sidecar session today (task 045); it gets the command line's layers
(`ExchangeBackends` over the one `ExchangeTokenSource` of task 049), so its `mailbox-list` request answers from the
Admin API without the sidecar and the other requests keep the warm session.
Why: the command line's list runs in 7.5 s without a pwsh child since task 048; the window still pays the connect
and then the cmdlet for the same list.
Done when: the window's shared list answers with no sidecar start in the `window:` log line, the access request
still uses the warm session, and the `--smoke` run starts no sidecar.
