---
status: idea
size: S
origin: done/046-window-specific-sign-in-hints.md
created: 2026-10-30
milestone: M4 Window
---

# The window's own hints for the failures after the sign-in

What: the failures that reach the window after a sign-in still carry the command line's hints (Graph's 401 says
`run m365-aegis login`, the sidecar's failure says `run m365-aegis doctor`); the window's hint set of task 046 gets
a line for each of them ("Sign in again", "the Exchange Online tools on this computer need a check; ask the
administrator") and the cards show them.
Why: the same reason as 046 (D-001): a hint that names a command the colleague cannot run is noise.
Done when: every failure kind the bridge can return has a window hint, held by a test over the hint sets, with the
command-line snapshots unchanged.
