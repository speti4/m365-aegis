---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
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

1. [ ] `index.css`: `font-sans` = Segoe UI Variable (with the system stack behind it), `font-mono` = Cascadia Mono,
       Consolas; the Geist import and package removed; the type scale (20 / 15 / 13 px) on the page title, the card
       titles and the body; a tinted page (`--background`) under white surfaces (`--card`) in light, the dark
       counterpart; `--radius` to 6 px; 32 px table rows
2. [ ] `plan-view.tsx`: the plan and the confirm step as a change ticket — a tinted surface with an amber
       (`--warning`) left rule, before → after rows with the after value strong, the stepper quiet; the result step
       stays as it is
3. [ ] The card descriptions capped near 70ch or trimmed to one line where they run long; identifiers (ids,
       addresses, the plan record) in mono, nothing else
4. [ ] Tests where a class is asserted; the front-end checks (`npm-licenses.py`, `check-vulnerable.py` for the
       package change); the mock in light and dark; `docs/images/window-group-members.png` retaken; docs under
       *Docs that move*

## Log

- 2026-11-02 — promoted on `task/127-design-direction-typography-and-change-ticket` by the main session

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
