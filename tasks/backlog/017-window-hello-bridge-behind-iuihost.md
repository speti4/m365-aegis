---
status: idea
size: M
origin: session 2026-10-28
created: 2026-10-28
milestone: M4 Window
---

# The window's first real page: the sign-in state and a lookup over the bridge, behind `IUiHost`

What: on the host the spike picks (task 014), the front end gains its first real page: the sign-in state from the
core (`whoami` over the bridge), a group lookup (`group members`) with the table rendered in shadcn/ui components,
and the error path (an `error` reply shown as a toast); the bridge gains request and reply types for these, the
samples under `tests/contracts/bridge` grow with them, and both test suites read them.
Why: the window's first page that does work, and the proof that the bridge carries the core's answers.
Done when: the page shows the signed-in account and a group's members live, the `--smoke` run still passes, and
the C# and Vitest contract tests agree on the new messages.
