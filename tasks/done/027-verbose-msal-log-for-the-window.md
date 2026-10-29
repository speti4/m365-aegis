---
size: S
origin: done/025-msal-logging-behind-verbose.md
created: 2026-10-29
started: 2026-10-29
plan: none
closed: 2026-10-29
milestone: M4 Window
---

# 027 — Verbose MSAL logging for the window

## Context

The window builds its own MSAL client, and `--verbose` of task 025 is a command-line option only; the
window gets the same switch (a flag of `m365-aegis-gui`, or a setting in the window) that registers
`WithLogging` with PII off into the same `logs\` folder through the same scrubbing sink, and writes nothing
without it.
Why: a failed sign-in on a support colleague's machine is diagnosed from the log, and the colleague runs the
window, not the command line.
Done when: a verbose window sign-in writes a token-free log next to the command line's, and the test over the
sink covers the window's lines too.
Part of the Window bundle (017+030, 027, 028, 031, 029) of the round of 2026-10-29, after the window holds the
core's auth (task 017); the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, the window and the logging: the switch
- `README.md`: the window's `--verbose`, one line

## Not in this change

- a log viewer in the window

## Steps

1. [x] The `--verbose` flag of `m365-aegis-gui` (or a setting), the same sink into the same `logs\` folder,
       nothing written without it
2. [x] The sink test covers the window's lines
3. [x] Live check (main session): a verbose window sign-in writes a token-free log
4. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Window bundle (017+030, 027, 028, 031, 029)
- 2026-10-29 — `m365-aegis-gui --verbose` calls `LogSink.EnableVerbose(home)` before the dispatcher builds any
  MSAL client, as the command line's `Program` does, so every broker the window makes
  (`MsalTokenBroker.Create(profile, home, parentWindow)`, which reads `LogSink.Verbose`) logs through the same
  scrubbing sink with PII logging off into the same `logs\` folder; without the flag no callback is registered.
  An unexpected exception of a bridge request goes to the same log as `internal-error` (task 017).
- 2026-10-29 — `LogSinkTests.TheWindowsLinesReachTheFileWithoutATokenOrAnAccount`: lines shaped like the
  window's run (the broker with the window as the picker's parent, the silent sign-in with a home account id, an
  interactive call's `login_hint`, a token response, the bridge's internal-error stack trace) through the MSAL
  callback and the sink: no token, account, tenant or GUID reaches the file; the step, the window handle and the
  failing frame stay.

## Deviations from the plan

- none

## Verification

- 2026-10-29 — offline, by the task session: the Release window started with `--verbose` against an empty
  `M365AEGIS_HOME` (no profile, so no MSAL call) left no `logs\` folder behind: nothing is written without a
  sign-in. `LogSinkTests` green.
- 2026-10-29 ~22:03 (UTC+1) — live, in the main session's signed-in round: the window with `--verbose` wrote
  1734 lines under its process id into `logs\m365-aegis-2026-10-29.log`, all `msal:`; a scan for `eyJ`, the tenant
  domain, the tenant id, the client id, both UPNs, the display name and the object id found 0 hits each; a second
  scan by the main session before the merge found no raw GUID in the file and `<guid>` on 852 of the window's
  lines (MSAL's correlation ids, scrubbed). The run without `--verbose` (a lookup, then closed) wrote 0 lines.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
