---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# A token-hygiene test: a marked fake token never reaches a log, a plan record, an envelope or stderr

What: the test auth service hands out tokens that carry a unique marker; the test drives `login`, `whoami`, a
`--verbose` run with the MSAL log on, a plan apply with `--yes`, a failed apply and an error envelope, then scans
every file under the temp home folder (`logs\`, `plans\`, `config.json`) and the captured stdout and stderr for the
marker; the sidecar's `Hide-Secret` is covered on the Pester side with the same marker in a `Connect` reply.
Why: SECURITY.md's first scope item is a token written to a log, a plan record, an envelope or stderr; the scrubbing
exists in several places (the MSAL log filter, the plan record, the sidecar) and no test holds them together.
Done when: the test is green, and injecting the marker into any of those sinks on purpose makes it red.
