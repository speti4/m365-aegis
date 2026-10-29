---
status: idea
size: M
origin: done/015-sidecar-protocol-allow-list-and-connect.md
created: 2026-10-29
milestone: M4 Window
---

# The window holds one `ExchangeSession` through the bridge

What: the window starts one sidecar on the first mailbox request and keeps it alive (`ExchangeSession` of task
015 is built for it: connect with the broker's token, `reconnect` before the expiry, a dead sidecar replaced), the
bridge gains the mailbox requests the window's pages need, and the session is closed with the window.
Why: every command-line mailbox lookup pays the 2.4 s cold connect (task 011); a warm sidecar in the window
answers in the cmdlet's own time, which is what the roadmap's "warm sidecar" of M4 means.
Done when: two mailbox lookups in a row from the window start one pwsh process, the second answers without a
connect, and the `--smoke` run still starts no sidecar.
