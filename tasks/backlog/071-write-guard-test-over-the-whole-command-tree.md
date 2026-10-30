---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# A write-guard test over the whole command tree: without `--yes`, nothing is sent

What: one table-driven test in the Cli tests that runs every write command (`group members|owners add|remove`,
`mailbox access grant|revoke`, `mailbox forwarding set|clear`, `call` with a non-GET method, each CSV form and
`--resume`) without `--yes` and with `--dry-run`, and asserts that the fixture transport recorded no request other
than GET and the fake sidecar no cmdlet outside the read list, while the exit code is 7 or 0 and the plan is printed.
Why: SECURITY.md names a write that happens without a confirmation as a vulnerability (D-006); today each command's
tests cover its own guard, and a new write command could land with none.
Done when: the test enumerates the write commands from the command tree itself, so a new write command joins it
without an edit, and it is green.
