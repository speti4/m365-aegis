---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
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

1. [x] `App.tsx`: a sticky header (`sticky top-0`, a background and a bottom border) with the title, the version,
       the status dot + account + tenant from the sign-in state, Sign in / Sign out, and the group links (`href="#…"`
       to ids on the card groups, smooth scroll); the sign-in card keeps the details (profile, tokens, device code)
2. [x] `sign-in-state.ts` / `sign-in-card.tsx`: the "signed in · Exchange unavailable" variant when the whoami reply's
       Exchange token row carries an error; the mailbox write cards show that state instead of staying plainly enabled
3. [x] `applied-writes.ts` + `mailbox-access-card.tsx` + `mailbox-write-cards.tsx` / `plan-view.tsx`: after a re-read
       (task 091) the result step shows the "read X again — show it" line, which scrolls the access card into view
4. [x] Tests (the bar's content per sign-in state, the Exchange variant, the show-it line and its scroll call);
       docs under *Docs that move*; the front-end checks; the mock at 1280 and 900 px wide, scrolled

## Log

- 2026-11-02 — promoted on `task/124-sticky-top-bar` by the main session
- 2026-11-02 — the bar: `components/top-bar.tsx` (`TopBar`, `pageGroups`, `shortTenant`), sticky (`sticky top-0`,
  a background and a bottom border), full width, one row at 900 px and wider: the title, the version, a short
  "mock" badge in a browser, the links Look up · Snapshots · Change, and the sign-in from `App`'s `signIn` state:
  a dot (the bar's one color), the state in words, the account, `tenant 00000000…` (the whole id as the title) and
  the profile (both from 1024 px up; the sign-in card has them anyway), Sign in (the broker; the device code stays
  on the card) or Sign out. The state links to the sign-in card. `App.tsx` puts the cards into sections
  (`#sign-in`, `#look-up`, `#snapshots`, `#change`) with a small heading each; a link calls `reveal`
  (`lib/scroll.ts`: `scrollIntoView` smooth, `auto` under `prefers-reduced-motion`, then the focus to the section
  without a second scroll) and does not change the address, since the dev sketch's route is a hash
- 2026-11-02 — the Exchange state: `exchangeError(state)` and `statusLabel(state)` in `sign-in-state.ts`; the
  sign-in card's badge and the bar read "signed in · Exchange unavailable" when the whoami reply's `exchange` row
  carries an error (the core always sends that row, `AuthService`); the write cards take an `exchangeError` prop,
  show an "Exchange Online unavailable" notice with the row's reason and what to do, and keep "See the plan"
  disabled (the plan reads the mailbox through Exchange Online, which needs that token). The browser mock's default
  account now has an Exchange Online token; `?mock=no-exchange` starts it without one (whoami only)
- 2026-11-02 — the "Show it" line: `AppliedWrite` carries an optional `onReRead` callback of the write card that
  applied it; the mailbox access card calls it once its read again has answered with the holders (`run` returns
  whether it did) with its title, the mailbox's display name and `show` (`reveal` of the card, which takes the focus
  with `tabIndex={-1}`). The write card's result step shows "The Mailbox access card read Finance again after this
  change." and "Show it"; a read that failed, or an answer that belongs to an earlier apply, shows nothing
- 2026-11-02 — tests: `top-bar.test.tsx` (the bar per sign-in state, the Exchange variant, the links' scroll calls
  and `preventDefault`, `reveal` with and without reduced motion, `shortTenant`), `applied-writes.test.tsx` (the
  line, `scrollIntoView` asserted on the access card with `{ behavior: "smooth", block: "start" }`, the focus, no
  line after Done and an apply elsewhere, none after a failed read), `App.test.tsx` (the sections, the bar's state,
  the Exchange-unavailable page in the window), `transport.test.ts` (`?mock=no-exchange`); docs: `usage.md` "The
  window", `development.md` (the mock flag, the top bar and the "Show it" line)
- 2026-11-02 — follow-ups: none found; the pull request lists none
- 2026-11-02 — merged as #177 (squash, `f5076ac`); closed on `main` by the main session

## Deviations from the plan

- The write cards do not just say Exchange is unavailable, they also disable "See the plan": the plan reads the
  mailbox through Exchange Online, so it could only fail.
- The browser mock's default state changed: its account had no Exchange Online token (an error row), which would
  now block the write cards in the default mock; the default has a token and `?mock=no-exchange` shows the error.
- The "Show it" line says "…again after this change." with a "Show it" link button instead of "— show it".
- `docs/images/window-group-members.png` is not retaken: its frame is the group members card alone, without the bar.

## Verification

- 2026-11-02 — the browser mock (`npx vite --port 5175`, Playwright), at 1280 and 900 px wide and 715 px high:
  scrolled to the bottom the bar stays at the top (its top at 0 px); each link puts its group's heading 64 px from
  the top, under the 45 px bar, moves the focus to the section and leaves the address as it was. With the keyboard:
  Tab reaches Look up, Snapshots, Change, the state and Sign out; Enter on Change, then Tab, lands on the rights
  card's Grant. After Reception's lookup on the access card and a FullAccess revoke for bob on the rights card, the
  result step reads "The Mailbox access card read Reception again after this change." and "Show it" brings the
  access card 80 px under the top with the focus on it and its "Read again after a change on the Mailbox rights
  card." line in view. `?mock=signed-out`: the bar reads "signed out" with the profile's account in plain text and
  Sign in, the write cards "Signed out" with "See the plan" disabled. `?mock=no-exchange`: the bar and the badge
  read "signed in · Exchange unavailable" (an amber dot), the token row the reason, the write cards "Exchange Online
  unavailable" with "See the plan" disabled. The screenshots are scratch files named `124-*.png`, outside the repo.
- 2026-11-02 — the checks: `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (24 files, 235
  tests, 100 % statements, branches, functions and lines), `npm run build`; `dotnet restore --locked-mode`,
  `dotnet format --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release -p:SkipFrontendBuild=true`
  (0 warnings), `dotnet test --no-build -c Release` (1,658 passed), `python -I scripts/changelog.py --check`,
  `python -I scripts/check-fixtures.py --self-test` (17 cases) and `python -I scripts/check-fixtures.py` (0
  findings). No C# changed, so no C# coverage run.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
