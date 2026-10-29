---
status: ready
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
milestone: M1 Foundation
---

# A dated note in D-003: the broker's silent path never reaches the file cache's refresh token

What: a "Note 2026-10-29" in `tasks/decisions/D-003-own-public-client-with-msal-and-wam.md`, next to the note of
task 013: with the broker enabled, MSAL.NET answers a silent call for an account the broker lists from the broker
alone and never falls back to the refresh token the file cache holds from a device-code sign-in, so the adapter
tries the same account through a second client over the same cache without the broker; the file cache
therefore holds the full token set of a device-code sign-in, not only accounts and id tokens.
Why: D-003 states what the file cache holds and why it is registered; task 002 measured a case that the
decision's text does not cover, and a decision file is the main session's to edit.
Done when: the note is in D-003 with the date and the measurement, and `tasks_check` is green.
