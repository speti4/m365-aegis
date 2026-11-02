---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 124 — A sticky top bar with the account, the tenant and the sign-in state, and in-page navigation

## Context

The header becomes a slim bar that stays at the top: a status dot, the account, the tenant shortened, Sign in /
Sign out, and links that scroll to the card groups (Look up · Snapshots · Change). The sign-in badge gets a
"signed in · Exchange unavailable" variant when a token row has an error, and the mailbox write cards say so. The
result step shows "The Mailbox access card read Reception again — show it", scrolling there (`AppliedWrites` knows
when the re-read happened). The sketch's left rail with one pane at a time (task 018) stays the larger option.
Why: the UX/UI review of 2026-11-02 (findings D-1, U-2, D-9, U-6): seven equal cards run to 2,000–4,000 px at a 715
px window, the write cards sit at the bottom, the re-read after a change happens off-screen, and the account scrolls
away before the moment of confirming.
Done when: the bar is visible at every scroll position in the mock, each link lands on its group, the Exchange
variant shows on the mock's missing-token state, and the "show it" line appears after an applied change.

## Docs that move

- `docs/usage.md`, "The window": the bar (account, tenant, state, the links), the Exchange-unavailable state, the
  "show it" line after a change
- `docs/images/window-group-members.png` only if the bar is in the picture's frame (retake from the mock, made-up
  data)

## Not in this change

- The left rail / one pane at a time (a later decision)
- The status color tokens (task 123): the status dot uses the existing variants; the confirmation's own account line
  (task 120)
- The vocabulary (task 122)

## Steps

1. [ ] `App.tsx`: a sticky header (`sticky top-0`, a background and a bottom border) with the title, the version,
       the status dot + account + tenant from the sign-in state, Sign in / Sign out, and the group links (`href="#…"`
       to ids on the card groups, smooth scroll); the sign-in card keeps the details (profile, tokens, device code)
2. [ ] `sign-in-state.ts` / `sign-in-card.tsx`: the "signed in · Exchange unavailable" variant when the whoami reply's
       Exchange token row carries an error; the mailbox write cards show that state instead of staying plainly enabled
3. [ ] `applied-writes.ts` + `mailbox-access-card.tsx` + `mailbox-write-cards.tsx` / `plan-view.tsx`: after a re-read
       (task 091) the result step shows the "read X again — show it" line, which scrolls the access card into view
4. [ ] Tests (the bar's content per sign-in state, the Exchange variant, the show-it line and its scroll call);
       docs under *Docs that move*; the front-end checks; the mock at 1280 and 900 px wide, scrolled

## Log

- 2026-11-02 — promoted on `task/124-sticky-top-bar` by the main session

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
