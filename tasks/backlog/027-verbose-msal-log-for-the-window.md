---
status: idea
size: S
origin: done/025-msal-logging-behind-verbose.md
created: 2026-10-29
milestone: M4 Window
---

# Verbose MSAL logging for the window

What: the window builds its own MSAL client, and `--verbose` of task 025 is a command-line option only; the
window gets the same switch (a flag of `m365-aegis-gui`, or a setting in the window) that registers
`WithLogging` with PII off into the same `logs\` folder through the same scrubbing sink, and writes nothing
without it.
Why: a failed sign-in on a support colleague's machine is diagnosed from the log, and the colleague runs the
window, not the command line.
Done when: a verbose window sign-in writes a token-free log next to the command line's, and the test over the
sink covers the window's lines too.
