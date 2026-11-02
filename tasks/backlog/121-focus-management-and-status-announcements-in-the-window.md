---
status: idea
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# Focus management and status announcements in the window, with card headings, radio groups and the stepper's semantics

What: the lookup field stays enabled while a lookup runs (`readOnly`, `aria-busy`, a double-submit guard) so focus
never falls to the body; a write card focuses the new step's heading or first control on a step change and the
mailbox field after Done; each card keeps one persistent `role="status"` text naming the transition ("Looking up
Finance…", "Finance: 4 direct members", "Plan: 2 changes", "Done: every change was made"), refusals stay alerts.
Card titles become `h2` (the cards regions); either/or choices use base-ui `RadioGroup` (one Tab stop, arrow keys);
the stepper's `aria-current` sits on the item with a spoken state; the picker's option name separates name and address.
Why: the UX/UI review of 2026-11-02 (A-1, A-3, U-7; A-5, A-6, A-11, A-12): every lookup and write step drops the
focus, nothing is announced to a screen reader, and the page goes from `h1` straight to `h3`.
Done when: a keyboard-only lookup and write flow keeps the focus on a named control at every step, axe reports no
`heading-order` hit, and the tests assert the status texts and the focus targets.
