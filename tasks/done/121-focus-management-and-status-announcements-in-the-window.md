---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
plan: none
milestone: M4 Window
---

# 121 — Focus management and status announcements in the window, with card headings, radio groups and the stepper's semantics

## Context

The lookup field stays enabled while a lookup runs (`readOnly`, `aria-busy`, a double-submit guard) so focus never
falls to the body; a write card focuses the new step's heading or first control on a step change and the mailbox
field after Done; each card keeps one persistent `role="status"` text naming the transition ("Looking up Finance…",
"Finance: 4 direct members", "Plan: 2 changes", "Done: every change was made"), refusals stay alerts. Card titles
become `h2` (the cards regions); either/or choices use base-ui `RadioGroup` (one Tab stop, arrow keys); the
stepper's `aria-current` sits on the item with a spoken state; the picker's option name separates name and address
and an empty list is not exposed as expanded.
Why: the UX/UI review of 2026-11-02 (findings A-1, A-3, U-7; A-5, A-6, A-11, A-12): every lookup and write step drops
the focus, nothing is announced to a screen reader, and the page goes from `h1` straight to `h3`.
Decided 2026-11-02: bundled with task 120 on one branch (the same files).
Done when: a keyboard-only lookup and write flow keeps the focus on a named control at every step, axe reports no
`heading-order` hit, and the tests assert the status texts and the focus targets.

## Docs that move

- `docs/development.md`, "The front end": one line on the status region and the focus rule, if the section
  describes the cards' parts (else none)
- the front-end tests for the status texts and the focus targets

## Not in this change

- The forced-colors and focus-ring contrast rules (task 123)
- The vocabulary of the announced texts beyond what is new here (task 122)
- The sticky top bar (task 124)

## Steps

1. [x] Lookup cards: `readOnly` + `aria-busy` instead of `disabled` on the field while busy, a guard against a second
       submit; the focus stays in the field after the reply
2. [x] `mailbox-write-cards.tsx` / `plan-view.tsx`: on a step change, focus the step's heading or first control
       (`tabIndex={-1}` targets, a `useEffect` on `state.step`); the mailbox field after Done and "Change the picks"
3. [x] One persistent `sr-only` `role="status"` per card, its text set on every transition; refusals keep
       `role="alert"`; the sign-in card's state changes announced the same way
4. [x] `ui/card.tsx`: `CardTitle` renders an `h2` (or `role="heading" aria-level=2`); each card a `section`
       `aria-labelledby` its title
5. [x] base-ui `RadioGroup`/`Radio` for the `Choice` groups and the mode toggle, styled with `buttonVariants`; the
       "List the shared mailboxes" button out of the radiogroup; the stepper's `aria-current` on the `li`, sr-only
       state, `aria-hidden` separators; the picker option's name and the empty-list `aria-expanded`
6. [x] Tests (focus targets, status texts, heading levels, arrow keys on a radio group); the front-end checks; an
       axe run on the mock (no `heading-order`); a keyboard-only walk

## Log

- 2026-11-02 — promoted on `task/120-121-window-flow-and-focus` by the main session, with task 120
- 2026-11-02 — the lookup cards (`useLookup` in `components/card-parts.tsx`, shared with task 120): the field is
  `readOnly` and the form `aria-busy` while a lookup runs, never `disabled`; a second submit while one runs is
  dropped (a ref, so two Enters in one tick send one request); the buttons that a lookup disables ("Look up",
  "Compare", "List the shared mailboxes", "Read again") keep their focus through `focusableWhenDisabled`; a pick from
  the shared mailboxes and "Read again", whose buttons go with the answer, put the focus back in the field. The user
  picker takes `busy` (read-only) instead of `disabled`.
- 2026-11-02 — the write cards: `WriteCard` keeps a step ref and a `useEffect` on `state.step` focuses the step's
  first element (`tabIndex={-1}`): the working line while planning or applying, the plan's title, the confirmation's
  sentence, the refusal's or the result's notice, the mailbox field (`pickTarget`) on the picks after "Change the
  picks", Cancel, Escape or "Start a new change"; the first render and a reset by a sign-out move nothing.
- 2026-11-02 — `LiveStatus`: one sr-only `role="status"` paragraph per card, always mounted; the texts: "Looking up
  Finance…", "Finance: 4 direct members", "Reception: 3 grants", "Bob Example: 3 grants over 4 mailboxes", "2 shared
  mailboxes", the first lookup's Exchange Online note, the read-again and the stale mark's sentences,
  "dave@contoso.com: 4 groups, 2 sites, …", "8 added, 6 removed between the two snapshots", "Nothing to compare yet",
  "No snapshot yet"; `writeStatus` ("Computing the plan…", "Plan: 2 changes", "Plan: nothing to change", "Confirm: 2
  changes", "Applying the plan…", "Done: every change was made", "Partly done: 1 of 2 changes made", "Not done: no
  change was made"; nothing on the picks or a refusal, which is an alert); `signInStatus` for the sign-in card
  ("Checking the sign-in…", "Waiting for the sign-in: choose the account in the Windows sign-in window", "Signed in as
  alice@contoso.com"…). The user picker hands "N matching users: use the arrow keys to pick one." or "No user starts
  with …" to the card's region (`onAnnounce`).
- 2026-11-02 — `ui/card.tsx`: `Card` is a `section`, `CardTitle` an `h2`; every card passes `aria-labelledby` its
  title's `useId`. `Choice` (card-parts) is base-ui `RadioGroup` + `Radio.Root` styled with `buttonVariants`, used by
  both write cards and the access card's "A mailbox / A user"; "List the shared mailboxes" sits beside the group, out
  of it. The stepper: `aria-current="step"` on the `li`, sr-only "(done)" / "(current)", the `›` `aria-hidden`. The
  picker's option reads "Dave Example dave@contoso.com" (a space), the listbox is rendered only with users, and the
  field is `aria-expanded` and `aria-controls` only then.
- 2026-11-02 — tests: the focus target after each step (Enter, Continue, Escape twice, Cancel, apply, result, Start a
  new change, a refusal, a sign-out), the status texts of every card, `writeStatus` and `signInStatus` over every
  state, the heading and its `aria-labelledby`, the arrow keys on both radio groups (ArrowRight moves and checks, one
  Tab stop), the stepper's text and separators, the picker's `aria-expanded`, `aria-controls` and announcements.

- 2026-11-02 — rebased onto `main` after task 123 (#178): every element the page focuses on its own (the plan's title,
  the confirmation's sentence, the working line, the result's and the refusal's notice, the page's group sections, the
  access card that "Show it" reveals) carries `focusTarget` (`src/lib/focus.ts`): `outline-none` for a mouse, 123's
  2 px `outline-ring` under `focus-visible`, so a keyboard user sees where the focus went. Walk: each step's target
  shows the 2 px outline; axe 0 violations signed in and signed out (the 4.02:1 badge is gone with 123's tokens).

- 2026-11-02 — merged as #179 (squash); closed on `main` by the main session; no follow-up

## Deviations from the plan

- The working line ("computing the plan", "applying") is a focus target too, so the focus never falls to the page
  between "See the plan" and the plan, or between the apply and the result.
- `docs/development.md` "The front end" got a paragraph on the cards' shared parts (the status region, the focus rule,
  the headings, the choice), since the section describes the cards.
- Card titles are `h2` as planned. Task 124, merged meanwhile, groups the cards under `h2` group headings (Look up,
  Snapshots, Change), so a card's `h2` sits beside its group's rather than under it; axe has no `heading-order` hit,
  and the outline stays navigable. Card titles one level lower would need a heading for the sign-in section and the
  answers' sections at `h4`; left as it is.

## Verification

- 2026-11-02 — the checks of task 120's Verification (the same runs: 243, then 253 after the rebase, front-end tests at 100% coverage, lint,
  typecheck, build; the .NET and script checks).
- 2026-11-02 — keyboard walk of the browser mock (Playwright, port 5173), `document.activeElement` and the card's
  status after each step: group lookup — the field (`Looking up Finance…`, then `Finance: 4 direct members`); a miss —
  the field, status empty, the alert; mailbox lookup — the field (with the session note, then `Finance: 3 grants`);
  ArrowRight on "A mailbox" — "A user" checked and focused, the next Tab goes to "List the shared mailboxes"; rights
  card: Enter → the plan's title (`Plan: 1 change`), Continue → "You're about to make this change as
  alice@contoso.com (profile work):" (`Confirm: 1 change`), Escape → the plan's title, the apply → "Applying the
  plan…" then the result notice (`Done: every change was made`), Tab → "Start a new change", Enter → the Mailbox
  field; forwarding: Cancel and Escape → the Mailbox field. Sign-in card: "Signed out" → "Waiting for the sign-in:
  choose the account…" → "Signed in as alice@contoso.com".
- 2026-11-02 — axe-core 4.10.2 (cdnjs, `addScriptTag`) on the mock: 0 violations on the initial page and 0 after a
  lookup error, three answers, a diff and a forwarding confirmation; headings H1, then H2 per card, H3 inside the
  answers (no `heading-order`); after the rebase onto task 124 again 0 violations, headings H1, the group and card
  H2s, H3 inside the answers. Signed out (`?mock=signed-out`) the one finding is `color-contrast` on the
  "signed out" destructive badge (4.02:1), the existing badge colors that task 123 changes.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
