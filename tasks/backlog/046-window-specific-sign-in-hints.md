---
status: idea
size: S
origin: done/040-window-sign-in-over-the-bridge.md
created: 2026-10-30
milestone: M4 Window
---

# The window's sign-in failures get their own hints

What: the failure hints of `SignInFlow` name the command line (`run m365-aegis login again`, the `login --tenant …`
line when no profile exists) and the window's sign-in card shows them as they are; the window gets hints written
for a person in front of the card ("Sign in again", "ask the administrator for the tenant profile"), chosen by the
host (`IUiHost`) or by a hint set per surface, with the command line's text unchanged.
Why: a support colleague reads the card, not a shell (D-001); a hint that names a command they cannot run is noise.
Done when: every failure the Core tests cover shows a window hint in the card and the command-line snapshots are
unchanged.
