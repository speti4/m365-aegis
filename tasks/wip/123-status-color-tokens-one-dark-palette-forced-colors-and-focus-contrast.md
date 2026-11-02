---
size: S
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 123 — Status color tokens, one dark palette, forced colors and the focus ring's contrast

## Context

`--success`, `--warning`, `--danger` tokens with soft backgrounds and badge variants, used for the sign-in state,
the plan's actions, the diff's added and removed rows and the per-change results, replacing the hard-coded amber
and emerald. One dark palette: the `dark` variant follows `prefers-color-scheme` (a `@custom-variant` on the media
query, or `.dark` from `matchMedia`), the two token blocks merge, next-themes goes unless it is wired. Forced colors:
`outline-hidden` for `outline-none` on button and input, `forced-colors:` rules and a check icon on a selected
choice or right, a border on the card. Contrast: a darker `--ring` and `--input` in light, a lighter ring in dark,
no `/50` ring, the destructive badge above 4.5:1, a disabled button that looks disabled, a styled checkbox.
Why: the UX/UI review of 2026-11-02 (findings D-4, D-5, D-6, A-2, A-7, A-9, D-12): added and removed look the same,
every `dark:` utility is dead, and in high-contrast mode a write card shows neither the selected choice nor the focus.
Decided 2026-11-02 (D-015): the direction is the Windows-native operations console; the status triad's hues follow
it (green, amber, red as tokens; one steel-blue accent for selection and focus). The typography, the surfaces and the
change ticket are task 127, on top of this one.
Done when: light, dark and forced colors show the states, focus and selection apart; axe clean; no dead `dark:` class.

## Docs that move

- `docs/development.md`, "The front end": the token set and the dark rule in one or two lines; the next-themes
  line goes if the package goes
- `gui/web/package.json` + the lock file if next-themes goes (the license check runs; the front-end package reasons
  in `docs/development.md` lose its line)

## Not in this change

- The font, the type scale, the page tint and the change-ticket surface (task 127)
- The inline lookup errors, the labels and the confirmation's content (task 120); the focus targets (task 121) —
  this task only makes the focus visible
- The vocabulary (task 122)

## Steps

1. [ ] `index.css` / `styles/shadcn-tailwind.css`: `--success`, `--warning`, `--danger` (+ soft backgrounds and
       foregrounds) in light and dark; the accent for `--ring`/`--primary` selection per D-015; one dark token block
       reached by `prefers-color-scheme` and a `dark` variant that matches it (`@custom-variant dark (@media …)` or
       `.dark` on `<html>` from `matchMedia`); `main.tsx` and `ui/sonner.tsx` without next-themes if it is unwired
2. [ ] `ui/badge.tsx`: `success`, `warning`, `danger` variants on the tokens; the destructive text above 4.5:1 in
       light; used in `sign-in-card.tsx`, `plan-view.tsx` (action badges, result icons), `snapshot-diff-card.tsx`
       (added/removed), `mailbox-access-card.tsx` (partial)
3. [ ] `ui/button.tsx`, `ui/input.tsx`: `outline-hidden` instead of `outline-none`, a ring that reads at ≥ 3:1
       (darker `--ring` in light, lighter in dark, no `/50`), a darker `--input` border; a disabled button that is
       muted, not pale primary; `forced-colors:` rules; `ui/card.tsx` `forced-colors:border`
4. [ ] The selected state of the `Choice` radios, the rights toggles and the mode toggle carries a check icon and
       `forced-colors:aria-checked:` colors; the confirmation checkbox styled (a shadcn `Checkbox` on base-ui)
5. [ ] Tests where a variant or a class is asserted; the front-end checks; the mock in light, dark and emulated
       forced colors (playwright `colorScheme` / `forcedColors`), axe clean; a grep that no unreachable `dark:` class
       is left

## Log

- 2026-11-02 — promoted on `task/123-status-tokens-and-theming` by the main session

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
