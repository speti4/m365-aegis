---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
plan: none
milestone: M4 Window
---

# 120 — Lookup errors shown in the card, labeled fields, Enter and Escape on the write cards, and a complete confirmation step

## Context

A failed lookup shows its error in the card (the write cards' `Notice` and `ErrorText` parts), clears or dims the
old answer and marks the field invalid; the page-level toast keeps a close button. A visible label above every
field (Mailbox, Users, Forward to, From, To); the write cards' fields sit in a form, so Enter means "See the plan"
and Escape goes back a step. The confirmation shows what the plan showed (the action badge, the from → to line,
the warnings, the account the change runs as), its button names the action (grant, revoke, start or stop
forwarding; destructive for a removal), and a Cancel returns to the first step.
Why: the UX/UI review of 2026-11-02 (findings U-1, D-3, A-4; U-3, D-7, A-8, U-8, A-10; U-5, U-6, C-2, D-2): after a
misspelled name the previous group's members stay on the card as the answer, a filled write card does not say which
field is the mailbox, and the last look before a change shows less than the plan did.
Decided 2026-11-02: bundled with task 121 on one branch, because both rewrite the lookup cards, the write cards and
`plan-view.tsx`.
Done when: a keyboard walk of the mock shows the error under the field and no stale table, labels, Enter and Escape
on the write cards, a confirmation that names the action and the account; the tests and usage.md "The window" match.

## Docs that move

- `docs/usage.md`, "The window": the error under the field, the labels, the confirmation's content and its button
  names, Cancel
- the front-end tests (`gui/web/src/components/*.test.tsx`) for the quoted strings and the new behaviors

## Not in this change

- The vocabulary sweep (task 122): only the strings this task introduces are new; existing terms stay as they are
- The status color tokens (task 123): a destructive button uses the existing `destructive` variant
- The sticky top bar and the account in the header (task 124); the confirmation names the account from the sign-in
  state the page already has
- The user picker on the write cards (task 125)

## Steps

1. [x] Lookup cards (`group-members-card.tsx`, `mailbox-access-card.tsx`, `user-access-card.tsx`,
       `snapshot-diff-card.tsx`): the error inline under the form with `Notice`/`ErrorText`, the old result cleared or
       dimmed when a new lookup starts, `aria-invalid` + `aria-describedby` on the field; `App.tsx`: the toast with a
       close button, kept for page-level errors only
2. [x] A `Label` part (shadcn, one file under `components/ui/`) and a visible label above every field of every card
3. [x] `mailbox-write-cards.tsx`: the picks inside a `<form onSubmit>` (Enter = "See the plan"), Escape = back on the
       plan and confirm steps (not while applying)
4. [x] `plan-view.tsx` `ConfirmView`: reuse `ChangeLine` (action badge, from → to), the warnings in compact form, the
       account and profile the change runs as, the button named by the operation, destructive for a removal, Cancel
5. [x] Tests for each; docs under *Docs that move*; the front-end checks; a keyboard walk of the mock

## Log

- 2026-11-02 — promoted on `task/120-121-window-flow-and-focus` by the main session, with task 121
- 2026-11-02 — `components/card-parts.tsx` (shared with task 121): `Field` (a shadcn `Label` above the control, tied
  by `useId`), `LookupError` (the write cards' `Notice` + `ErrorText`, an alert with an id), `AnswerFor` ("Answer for
  "Finance""), `SubmitButton` (base-ui `focusableWhenDisabled`, `aria-describedby` an sr-only "Type a group's name,
  address or object id first.") and `useLookup` (one lookup at a time, the old answer dimmed with `aria-busy` while
  the next runs, dropped on an `error` reply, whose error shows under the form). The four lookup cards use it: the
  field gets `aria-invalid` and `aria-describedby` the error; they lost their `onError` prop, so a card's error never
  reaches the toast; `App.tsx` keeps the toast for the sign-in requests, with `closeButton` and 10 s.
- 2026-11-02 — `ui/label.tsx` added with `npx shadcn@4.23.0 add label` (no package, no dependency); visible labels:
  Group, Mailbox / User (the access card's choice names it), User, From, To, Only this user, Mailbox, Users, Forward
  to, and a visible "Rights" legend. The access changes card's placeholders became examples ("The one before", "The
  newest", "Optional: a principal name or address").
- 2026-11-02 — the write cards: the picks are a `<form>` (Enter = "See the plan"; Enter on a rights toggle or a
  choice option submits through `submitOnEnter` instead of toggling it), "See the plan" stays focusable when it waits
  and the line beside it says "Still needed: the mailbox, at least one user." (a sign-in first when signed out);
  Escape on the card's content dispatches `back`, which the reducer honors on the plan, the confirmation and a
  refusal only, so an apply under way stays.
- 2026-11-02 — `ConfirmView`: "You're about to make these 2 changes as alice@contoso.com (profile work):" (the
  account from the sign-in state, `runAs`), the plan's title, each change through the plan's `ChangeItem` (the action
  badge, now → after with the after state `font-medium text-foreground`, in the plan too), the warnings in a compact
  "Good to know" list (`windowWarning`), "I've reviewed these changes.", the button from `confirmAction` in
  `plan-wording.ts` ("Grant permissions", "Revoke permissions", "Start forwarding", "Stop forwarding"; destructive
  for the revoke and the clear; "Confirm and apply" for an unknown operation), "Back to the plan" and a ghost
  "Cancel" (reset to the picks); the result's button is "Start a new change".
- 2026-11-02 — tests: every card test finds its fields by the visible label (`field` in `src/test/dom.ts`), the
  inline error, `aria-invalid`/`aria-describedby`, the dropped old table, the empty and the double submit, Enter and
  Escape, the confirmation's lead, badges, warnings, button names and variants, Cancel; `confirmAction` and `count`
  in `plan-wording.test.tsx`; the bridge-sample sweep renders the confirmation with every mailbox warning. Docs:
  usage.md "The window" (labels, Enter, the inline error, the confirmation and its buttons, Cancel, "Start a new
  change"), development.md "The front end" and the window's writes, SECURITY.md's name of the confirmation button.

- 2026-11-02 — rebased onto `main` after task 124 (#177): the write cards keep its `exchangeError` notice and the
  "Show it" line on the result (with "Start a new change" for its "Done"), the access card its `reveal` ref (a
  `section` now); the "Still needed" line names "an Exchange Online token" when the window has none, and the sign-in
  card's status says "Signed in as …; Exchange Online is unavailable". The checks and the walk below ran again on the
  rebased tree (253 tests).

- 2026-11-02 — rebased onto `main` after task 123 (#178): the plan's and the confirmation's action badges, the
  notices, the result icons, the stale and partial marks use 123's `success`/`warning`/`danger` tokens (the error
  notice `border-danger/50`, the same color as `destructive`); the choices carry 123's check on the selected option
  (now base-ui radios); a focusable-disabled button looks like 123's disabled one (`aria-disabled:` in
  `ui/button.tsx`). Checks, keyboard walk and axe ran again on the rebased tree (260 tests, 100%).

- 2026-11-02 — merged as #179 (squash); closed on `main` by the main session; no follow-up

## Deviations from the plan

- The lookup cards' `onError` prop is gone rather than kept for page-level errors: no lookup error is page-level, and
  the toast stays for the sign-in requests only.
- "Still needed: …" is a visible line beside "See the plan", not only an `aria-describedby` text, so a mouse user sees
  it too; on the lookup cards the same hint is screen-reader-only, the empty field being self-evident.
- The look of a button disabled but focusable lives in `ui/button.tsx`'s base classes (`aria-disabled:` beside 123's
  `disabled:` muted outline), since the rebase onto task 123; before it, a class at the call sites.
- SECURITY.md named the old "Confirm and apply" button; it now says "a write card's confirmation button".

## Verification

- 2026-11-02 — `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (243 tests before the rebase onto task 124, 253 after it; 100% statements,
  branches, functions and lines), `npm run build` clean; `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes`, `dotnet build -c Release -p:SkipFrontendBuild=true`, `dotnet test` (1658 passed),
  `python -I scripts/changelog.py --check`, `check-fixtures.py` (self-test and 539 files, 0 findings) clean.
- 2026-11-02 — keyboard walk of the browser mock (Playwright, port 5173): "Finance" + Enter, then "Fnance" + Enter: the
  focus stays in the field, the table is gone, "The lookup failed / no group named 'Fnance' / What to do … /
  not-found" under it, the field `aria-invalid`; a mailbox lookup keeps the focus in the field; the rights card filled
  by Tab and planned by Enter on "Send As" (no toggle); Continue → Escape → back to the plan; a revoke behind a red
  "Revoke permissions", the result, "Start a new change"; the forwarding card planned by Enter in "Forward to", the
  confirmation with both warnings, "keeps no copy" and "as alice@contoso.com (profile work)", Cancel back to the
  picks, Escape on the plan; a disabled "See the plan" focusable with "Still needed: the mailbox.".

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
