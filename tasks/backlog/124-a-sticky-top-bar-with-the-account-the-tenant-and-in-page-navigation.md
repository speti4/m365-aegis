---
status: idea
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# A sticky top bar with the account, the tenant and the sign-in state, and in-page navigation

What: the header becomes a slim bar that stays at the top: a status dot, the account, the tenant shortened, Sign in
/ Sign out, and links that scroll to the card groups (Look up · Snapshots · Change). The sign-in badge gets a
"signed in · Exchange unavailable" variant when a token row has an error, and the mailbox write cards say so. The
result step shows "The Mailbox access card read Reception again — show it", scrolling there (`AppliedWrites` knows
when the re-read happened). The sketch's left rail with one pane at a time (task 018) stays the larger option.
Why: the UX/UI review of 2026-11-02 (D-1, U-2, D-9, U-6): seven equal cards run to 2,000–4,000 px at a 715 px window,
the write cards sit at the bottom, the re-read after a change happens off-screen, and the account scrolls away.
Done when: the bar is visible at every scroll position in the mock, each link lands on its group, the Exchange
variant shows on the mock's missing-token state, and the "show it" line appears after an applied change.
