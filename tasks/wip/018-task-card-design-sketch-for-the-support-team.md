---
size: S
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
milestone: M4 Window
---

# 018 — Task card design sketch: the plan-and-confirm flow for the support team, before the first card

## Context

A design sketch of the window's task cards with made-up data: the card list (grant FullAccess on a shared
mailbox, add a group owner, check a user's access), one card's steps (pick the objects, see the plan the core
computed, confirm, see the result), the states (signed out, working, refused, partial), light and dark, in the
shadcn/ui components; reviewed with one support colleague before a card is built.
Why: the team's workflow is the reason the window exists (D-001); the flow is cheaper to change on a sketch.
Done when: the sketch runs in the browser with the mock transport, the colleague's remarks are in this task's
Log, and the first card task is written from it.
Part of the window M4 bundle (040, 045, 018) of the round of 2026-10-29; the task session builds the sketch
behind the mock transport only (no bridge request, no Core change: the plan shape it shows is made up and marked
so, the real `Plan` type comes with M3), the colleague's review is the maintainer's step after the merge and its
remarks land in this Log then.

## Docs that move

- `docs/development.md`, "The front end": the sketch, where it lives and how it runs with the mock transport

## Not in this change

- a real task card wired to the bridge or a Core plan (M3 defines the plan, a later task builds the card)
- the colleague's review itself (the maintainer's, after the merge)

## Steps

1. [x] The card list and one card's steps with made-up data behind the mock transport: pick, plan, confirm, result
2. [x] The states: signed out, working, refused, partial; light and dark
3. [x] Front-end tests for the sketch's state machine; lint, typecheck and build green; the published window unchanged
4. [x] Docs
5. [ ] The colleague's review (the maintainer's, after the merge): the remarks in this Log, the first card task
   written from it

## Log

- 2026-10-29 — promoted by the main session into the window M4 bundle (040, 045, 018)
- 2026-10-29 — the sketch under `gui/web/src/sketch/`: `task-card-flow.ts` (the flow as a reducer: list, pick,
  planning, plan or refused, confirming, applying, result; signed out closes and locks the cards; the made-up plan
  and outcome types, marked SKETCH ONLY), `sketch-data.ts` (three cards — grant FullAccess on a shared mailbox, add a
  group owner, check a user's access — the made-up core that plans only what is not already so, refuses in the
  refusal scenario and fails the last change in the partial one), `task-card-sketch.tsx` (the page: a stepper Pick ›
  Plan › Confirm › Result, "Answer" for the read-only card, the confirmation with a checkbox, the result per change,
  controls for the sign-in, the core's answer and the theme). shadcn/ui's card, button and badge, lucide icons, no
  new package; the theme switch uses `.dark` and a new `.light` token block in `index.css`.
- 2026-10-29 — dev only: `main.tsx` reaches the sketch at `#/sketch/task-cards` under `import.meta.env.DEV`, a lazy
  import the build drops; `npm run build` output searched for the sketch's strings: none, one JS chunk as before.
- 2026-10-29 — tests: 11 Vitest cases in `sketch/task-card-flow.test.ts` (the whole flow and its step numbers, the
  plan only with every field, the refusal and back, back from the confirmation, no confirmation of an empty or
  read-only plan, signed out, late answers ignored, the made-up core's plan, answer, refusal and partial result).

## Deviations from the plan

- Native checkbox for the confirmation and buttons as radio and checkbox choices instead of shadcn's checkbox and
  select: no component generated for a sketch; the first real card adds what it needs.

## Verification

- 2026-10-29 — offline, by the task session: `npm run dev`, `#/sketch/task-cards`, driven with Playwright: the card
  list; in dark, "Add a group owner" with Finance EU, Bob and Carol, "working", the plan of two changes with its
  warning and the made-up-shape note, the confirmation, "Partly done" (1 of 2, the failure's sentence); in light,
  "Grant FullAccess" on Reception refused with the hint and back to the picks, and signed out (the notice, the cards
  locked). `npm run build`: no sketch string in `dist/`. Lint, typecheck, 55 Vitest cases green. The colleague's
  review is the maintainer's, after the merge.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
