---
status: idea
size: S
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# Status color tokens, one dark palette, forced colors and the focus ring's contrast

What: `--success`, `--warning`, `--danger` tokens with soft backgrounds and badge variants, used for the sign-in
state, the plan's actions, the diff's added and removed rows and the per-change results, replacing the hard-coded
amber and emerald. One dark palette: the `dark` variant follows `prefers-color-scheme` (a `@custom-variant` on the
media query, or `.dark` from `matchMedia`), the two token blocks merge, next-themes goes unless it is wired. Forced
colors: `outline-hidden` for `outline-none` on button and input, `forced-colors:` rules and a check icon on a
selected choice or right, a border on the card. Contrast: a darker `--ring` and `--input` in light, a lighter ring
in dark, no `/50` ring, the destructive badge above 4.5:1, a disabled button that looks disabled, a styled checkbox.
Why: the UX/UI review of 2026-11-02 (D-4, D-5, D-6, A-2, A-7, A-9, D-12): added and removed look the same, every
`dark:` utility is dead, and in high-contrast mode a write card shows neither the selected choice nor the focus.
Done when: light, dark and forced colors show the states, focus and selection apart; axe clean; no dead `dark:` class.
