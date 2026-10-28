---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Output contract (D-006): the envelope and the table for every command, exit codes, snapshot tests

What: every command renders through one path in the command line: the envelope (`EnvelopeJson`) in a pipe or
with `--json`, the table (`TableWriter`) at a terminal or with `--table`; a result type per command that both
renderings read; the `ExitCode` enum mapped from the core's failures (auth, not found, throttled, partial);
snapshot tests of both renderings with Verify (`Directory.Packages.props` names it) over scrubbed samples.
Why: D-006 is the contract shells, scripts and agents rely on; it has to exist before the lookups multiply.
Done when: the scaffold's `doctor` and the M1 lookups all render through it, a snapshot test guards each
rendering, and a wrong exit code fails a test.
