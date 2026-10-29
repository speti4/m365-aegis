---
status: ready
size: S
origin: done/004-output-contract-envelope-table-and-exit-codes.md
created: 2026-10-29
milestone: M1 Foundation
---

# An exit code for an unexpected internal error

What: today an unhandled exception ends `m365-aegis` with the runtime's exit code and a stack trace on stderr,
outside the output contract. Give the condition its own `ExitCode` value (a new number, D-006: existing values
never change meaning), catch it in `CommandRunner` (the one place every command runs through), render it as an
error envelope or an `error:` line like every other failure, keep the stack trace out of the envelope (a log
line under the home folder instead), and add the snapshot and exit-code tests.
Why: a script or an agent reading the envelope must never see a bare stack trace, and the exit code has to say
"the tool broke" apart from "the request failed".
Done when: a test that throws from a command gets the new code and a well-formed envelope, and `ExitCode` is
documented with the new value in `docs/development.md`.
