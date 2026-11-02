---
status: ready
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# Typography, surfaces and the change ticket of the design direction (D-015)

What: the window's look follows D-015. Segoe UI Variable, the system font, for the page (no web font:
`@fontsource-variable/geist` leaves `package.json`, with its license and `docs/development.md` lines); Cascadia Mono
or Consolas for identifiers only (object ids, addresses, the plan record); a 20 / 15 / 13 px scale with muted table
headers; a tinted light page with the dark counterpart; one steel-blue accent for selection and focus; 6 px radii,
32 px table rows. The plan and confirm step becomes a "change ticket": a tinted surface with an amber left rule,
before → after rows with the after value strong, and a confirm button colored by the action.
Why: D-015, decided 2026-11-02, after the UX/UI review's finding that the window reads as shadcn defaults and that
the one moment the tool exists for, the plan and the confirmation, has the weight of a form submit (D-2, D-8, D-12).
Done when: the mock shows the direction in light and dark on top of task 123's tokens, the README's window picture is
retaken from the mock, and the front-end checks pass with the web font gone.
