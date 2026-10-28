---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Spike: the Windows account manager from a console process, and where device code has to take over

What: a timeboxed try (one day) of the broker in the command line: the parent window from
`GetAncestor(GetConsoleWindow(), GetRootOwner)` through `WithParentActivityOrWindow`, in Windows Terminal,
conhost, the VS Code terminal and with stdout redirected (the tests' case); the silent `OperatingSystemAccount`
path on the Entra-joined machine; the behavior without a window (an SSH session, a scheduled task), where the
code falls back to device code; and whether the tenant's Conditional Access policies block device code. The
result shapes the fallback order of task 002 and a note in D-003.
Why: D-003 rests on the broker working from a console; the research marked it as the one auth spike.
Done when: a dated note lists each environment with the result, and task 002's fallback order is written from it.
