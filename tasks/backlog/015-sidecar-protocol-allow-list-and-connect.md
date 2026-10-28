---
status: idea
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M2 Exchange
---

# The sidecar in the core: the process, the protocol client, the allow-list, connect and reconnect

What: `Exchange/Sidecar` in the core starts `pwsh -NoProfile -NonInteractive -File sidecar\aegis-exo-sidecar.ps1`,
speaks the JSON-lines protocol the scaffold's script and its Pester tests define (`hello`, `connect`, `invoke`,
`exit`, the `error` replies), passes the broker's token (task 009) and renews it with a `reconnect` before the
expiry, maps a cmdlet's objects to typed records, and turns `cmdlet-not-allowed` and `cmdlet-failed` into the
exit codes of D-006; the allow-list is the one place a cmdlet is admitted. The window keeps one sidecar alive;
the command line starts one per run (a named-pipe daemon is a later idea).
Why: every mailbox command of M2 rides on this; the script exists, the core side does not.
Done when: the Core tests drive a real pwsh child against the fake module on Linux and Windows, a refused
cmdlet and a failed cmdlet come back as envelopes with the right codes, and `mailbox list` answers live.
