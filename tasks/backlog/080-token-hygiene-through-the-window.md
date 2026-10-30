---
status: idea
size: S
origin: done/072-token-hygiene-test-across-logs-plans-and-output.md
created: 2026-10-30
milestone: M6 Release and team
---

# Token hygiene through the window: the bridge's sign-in and the `window:` log lines

What: the marker-token test of task 072 run through the window's path: the bridge's sign-in request, a lookup over the
Exchange token source, an error reply, and the `window:` log lines, with every file under the temp home, the bridge
replies and the window's stderr scanned for the marker.
Why: task 072 covers the command line and the shared Core sinks; the window writes its own `window:` lines and sends
replies over the bridge, which are sinks of their own.
Done when: the test is green over the bridge dispatcher with the fake token source, and injecting the marker into a
reply or a `window:` line on purpose makes it red.
