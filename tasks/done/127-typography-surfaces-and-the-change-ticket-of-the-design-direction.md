---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
plan: none
milestone: M4 Window
---

# 127 — Typography, surfaces and the change ticket of the design direction (D-015)

## Context

The window's look follows D-015. Segoe UI Variable, the system font, for the page (no web font:
`@fontsource-variable/geist` leaves `package.json`, with its license and `docs/development.md` lines); Cascadia Mono
or Consolas for identifiers only (object ids, addresses, the plan record); a 20 / 15 / 13 px scale with muted table
headers; a tinted light page with the dark counterpart; one steel-blue accent for selection and focus (task 123 set
it); 6 px radii, 32 px table rows. The plan and confirm step becomes a "change ticket": a tinted surface with an
amber left rule, before → after rows with the after value strong, and a confirm button colored by the action
(task 120 named the button by the operation and made a removal destructive; this task gives the surface).
Why: D-015, decided 2026-11-02, after the UX/UI review's finding that the window reads as shadcn defaults and that
the one moment the tool exists for, the plan and the confirmation, has the weight of a form submit (D-2, D-8, D-12).
Decided 2026-11-02: on top of task 123's tokens (one `light-dark()` block in `index.css`); the same agent continues.
Done when: the mock shows the direction in light and dark on top of task 123's tokens, the README's window picture
is retaken from the mock, and the front-end checks pass with the web font gone.

## Docs that move

- `docs/development.md`, "The front end": the font and the scale, the change-ticket surface; the Geist reason line
  goes
- `gui/web/package.json` + the lock file (the font package goes; the license check runs)
- `docs/images/window-group-members.png`: retaken from the mock with made-up data (the README's picture)

## Not in this change

- The vocabulary (task 122)
- The data tables' header weight, `tabular-nums` and the diff tables' fixed layout (task 126, in parallel): this
  task sets the scale, 126 the header classes — coordinate at the rebase
- A left rail / one pane at a time

## Steps

1. [x] `index.css`: `font-sans` = Segoe UI Variable (with the system stack behind it), `font-mono` = Cascadia Mono,
       Consolas; the Geist import and package removed; the type scale (20 / 15 / 13 px) on the page title, the card
       titles and the body; a tinted page (`--background`) under white surfaces (`--card`) in light, the dark
       counterpart; `--radius` to 6 px; 32 px table rows
2. [x] `plan-view.tsx`: the plan and the confirm step as a change ticket — a tinted surface with an amber
       (`--warning`) left rule, before → after rows with the after value strong, the stepper quiet; the result step
       stays as it is
3. [x] The card descriptions capped near 70ch or trimmed to one line where they run long; identifiers (ids,
       addresses, the plan record) in mono, nothing else
4. [x] Tests where a class is asserted; the front-end checks (`npm-licenses.py`, `check-vulnerable.py` for the
       package change); the mock in light and dark; `docs/images/window-group-members.png` retaken; docs under
       *Docs that move*

## Log

- 2026-11-02 — promoted on `task/127-design-direction-typography-and-change-ticket` by the main session
- 2026-11-02 — type, surfaces, radii (`088d441`): `index.css` without the Geist import; `--font-sans` "Segoe UI
  Variable Text", "Segoe UI Variable", "Segoe UI", `system-ui`; `--font-mono` "Cascadia Mono", Consolas; the scale as
  Tailwind's own sizes in `@theme` (`text-xl` 20 px / 28, `text-base` 15 px / 22, `text-sm` 13 px / 20, the body's
  default), so every existing `text-sm` and the card title's `text-base` follow without a class change; the top bar's
  title `text-xl`. Light: page oklch(0.972 0.003 248) (#F4F6F8), text #1B1F24, muted #5B6470, line #DDE2E8 (as oklch),
  cards white; dark: page oklch(0.16 0.005 250), card oklch(0.215 0.006 250), the same cool hue; 123's accent and
  status triad kept. `--ticket` (light oklch(0.988 0.012 85), dark oklch(0.245 0.018 75)). `--radius` 6 px, the card
  `rounded-lg`; `CardDescription` `max-w-[70ch]`; `ui/table.tsx` rows 32 px (`TableHead` `h-8`, `TableCell`
  `py-1.5`), nothing else of the table. `@fontsource-variable/geist` uninstalled.
- 2026-11-02 — the change ticket (`96e81d9`): `ChangeTicket` in `plan-view.tsx`, `rounded-lg border border-l-4
  border-l-warning bg-ticket p-4`, around the plan (its title at 15 px, the changes, what is left, the warnings) and
  around the confirmation's lead, title, changes and warnings; the checkbox, the action's button (task 120) and the
  footnote below it; a plan with nothing to change stays plain. `ChangeItem` rows divided, the after state
  `font-semibold`. The stepper `text-xs`, 20 px circles, the done ones muted (forced colors keep 123's fill). Addresses
  in mono in the tables (group members' sign-in name, the access card's two address columns, user access's mailbox
  address). The result step untouched. `change-ticket.test.tsx` (3 tests).
- 2026-11-02 — docs (`bb0d863`): `docs/development.md` "The front end": a "The look" paragraph, the Geist part of the
  package reasons out; `docs/images/window-group-members.png` retaken from the mock (Group members answering
  "Finance", light, 1280 px, the card alone as before, 976 px wide).

- 2026-11-02 — merged as #181 (squash); closed on `main` by the main session; no follow-up

## Deviations from the plan

- The scale is set by redefining Tailwind's `text-sm`, `text-base` and `text-xl` in `@theme` rather than new classes
  at the call sites: one place, and the components (most of them shadcn's) keep their classes.
- The card border stays the 10 % ring: on the tinted page the white card separates clearly (looked at in the mock), so
  no 3:1 border.
- `licenses/allowed.json` keeps `OFL-1.1`: the allowlist is the policy, not the inventory.
- axe-core from cdnjs was not run (loading the remote script was refused in task 123's session too); the same
  hand-written contrast check as in task 123 ran instead.

## Verification

- 2026-11-02 — front end: `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (26 files, 263
  tests, 100% statements, branches, functions, lines), `npm run build` (no font file in `dist/`);
  `python -I scripts/npm-licenses.py` (20 shipped packages, 0 outside the allowlist), `python -I
  scripts/check-vulnerable.py` (0), `check-fixtures.py` (0 findings) and its self-test, `changelog.py --check`.
- 2026-11-02 — .NET: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the build (0 warnings),
  `dotnet test` 1658/1658 (no C# changed).
- 2026-11-02 — the mock (Playwright Chromium on Windows, 1280 px), light and dark: the page, a group answer
  ("Finance"), the rights card's plan and confirmation as the ticket, the signed-out page. The page resolves "Segoe UI
  Variable Text" (body 13 px, `h1` 20 px, card titles 15 px), Cascadia Mono present. Contrast of every visible text
  run over its painted background, with the diff, a partial lookup and a plan on the page: light 220 runs, lowest
  5.34:1; dark 220, lowest 5.86:1; signed out 99 each; none below 4.5:1.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
