---
status: idea
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
milestone: M1 Foundation
---

# MSAL's non-PII log into `logs\` behind a `--verbose` flag

What: once the command line has a logging path into `%LOCALAPPDATA%\m365-aegis\logs\`, the MSAL adapter
registers `WithLogging` with PII logging off and the library's lines go there under `--verbose` only; a WAM
error context may carry a tenant id in a service message, so the sink scrubs GUIDs before writing.
Why: a failed sign-in on a colleague's machine (a Conditional Access verdict, a broker error code) is
undiagnosable from the envelope's one sentence; the library's log names the step and the code.
Done when: a `--verbose login` writes a log file without any token, authorization code or user principal name
(a test over the sink with a scripted line set), and a run without the flag writes nothing.
