---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# An exit code contract snapshot: the `ExitCode` enum's names and values, frozen

What: a Verify snapshot of the `ExitCode` enum as `name = value` lines, one per member, plus a check that the exit
code table in `docs/development.md` lists the same pairs, so a renumbering, a renamed member or an undocumented
addition fails a test instead of a reader.
Why: the output contract (D-006) promises that exit codes never change meaning; a script or an agent branches on
them, and the parallel-session rounds already added codes on separate branches (8, 9, 70).
Done when: the snapshot is accepted, the docs check is green, and changing a value in the enum turns the test red.
