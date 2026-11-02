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

1. [x] `index.css` / `styles/shadcn-tailwind.css`: `--success`, `--warning`, `--danger` (+ soft backgrounds and
       foregrounds) in light and dark; the accent for `--ring`/`--primary` selection per D-015; one dark token block
       reached by `prefers-color-scheme` and a `dark` variant that matches it (`@custom-variant dark (@media …)` or
       `.dark` on `<html>` from `matchMedia`); `main.tsx` and `ui/sonner.tsx` without next-themes if it is unwired
2. [x] `ui/badge.tsx`: `success`, `warning`, `danger` variants on the tokens; the destructive text above 4.5:1 in
       light; used in `sign-in-card.tsx`, `plan-view.tsx` (action badges, result icons), `snapshot-diff-card.tsx`
       (added/removed), `mailbox-access-card.tsx` (partial)
3. [x] `ui/button.tsx`, `ui/input.tsx`: `outline-hidden` instead of `outline-none`, a ring that reads at ≥ 3:1
       (darker `--ring` in light, lighter in dark, no `/50`), a darker `--input` border; a disabled button that is
       muted, not pale primary; `forced-colors:` rules; `ui/card.tsx` `forced-colors:border`
4. [x] The selected state of the `Choice` radios, the rights toggles and the mode toggle carries a check icon and
       `forced-colors:aria-checked:` colors; the confirmation checkbox styled (a shadcn `Checkbox` on base-ui)
5. [x] Tests where a variant or a class is asserted; the front-end checks; the mock in light, dark and emulated
       forced colors (playwright `colorScheme` / `forcedColors`), axe clean; a grep that no unreachable `dark:` class
       is left

## Log

- 2026-11-02 — promoted on `task/123-status-tokens-and-theming` by the main session
- 2026-11-02 — tokens and theme (`1000ebf`): one `:root` block in `index.css`, every color
  `light-dark(<light>, <dark>)` under `color-scheme: light dark`; the old `.dark` block (neutral) and the media block
  (zinc, hue 285) merged into one neutral dark palette; the unused `chart-*` and `sidebar-*` tokens dropped. The
  `dark` variant is a custom variant on `@media (prefers-color-scheme: dark)` (or a `.dark` parent, minus a `.light`
  one: the dev sketch's switch). Status triad `--success`, `--warning`, `--danger` + `-soft`, `--destructive` =
  `--danger`; accent steel blue for `--primary` and `--ring` (light oklch(0.48 0.1 245), dark oklch(0.78/0.8 0.09
  240)); `--input` oklch(0.64) light (3.4:1 on white), oklch(0.52) dark (3.2:1 on the card); `--muted-foreground`
  oklch(0.52) / oklch(0.72). Badge variants `success`, `warning`, `danger` (+ a `CanvasText` border under forced
  colors). `next-themes` uninstalled; `ui/sonner.tsx` passes `theme="system"`; `main.tsx` had no provider to drop.
  Contrast of every pair computed before (oklch → sRGB): status text on the card and on its soft background 5.4–10.1:1.
- 2026-11-02 — components (`483485e`): plan actions add → success, remove/clear → danger, set/send → warning; result
  icons applied → `text-success`, failed → `text-danger`; diff added → success, removed → danger; sign-in signed in →
  success, signed out → warning; `partial` → warning, the stale notice `border-warning/50`; `Notice` and the sketch's
  copy on the tokens. Button and input: no `outline-none`, focus = `outline-2 outline-offset-2 outline-solid
  outline-ring` (no box-shadow ring); disabled button = `border-border bg-transparent text-muted-foreground` (no
  opacity); `forced-colors:aria-checked:` Highlight / HighlightText with `forced-color-adjust-none` on the button;
  a lucide `Check` (`data-icon="inline-start"`) on the selected `Choice`, right and lookup mode; card
  `forced-colors:border`; the stepper's done circles `CanvasText` under forced colors. New
  `status-colors.test.tsx` (7 tests) asserts the variants, the icons' classes and the check on the selected choice.
- 2026-11-02 — docs (`f07f920`): `docs/development.md`, "The front end": a "The theme" paragraph; the `next-themes`
  reason line removed.

## Deviations from the plan

- `outline-hidden` was tried and dropped: under emulated forced colors its `2px solid transparent` outline is painted
  on every button and input at rest (a double border everywhere, the focused control no longer apart). Without
  `outline-none` the browser paints no outline at rest anyway (its own one is `:focus-visible` only, which the
  `focus-visible:outline-*` classes replace), so focus is a real outline in every mode.
- The confirmation checkbox stays native instead of a base-ui `Checkbox`: the white square in dark came from the page
  declaring no `color-scheme`; with `color-scheme: light dark` Chromium draws it dark, `accent-primary` makes the check
  steel blue, forced colors handle it natively, and the three checkboxes' tests and bundle A's edits of the same lines
  keep their shape.
- `light-dark()` instead of a second dark block: one source per token. The build's Lightning CSS lowers it to two
  variables set by `color-scheme`, resolved on `:root`; the window follows the system's mode either way (checked on the
  built `dist/` through `vite preview`), only the dev sketch's `.light`/`.dark` pin is dev-server-only (noted in
  `index.css`).
- Signed out is amber (`warning`), not red: being signed out asks for an action, it is not a failure.

## Verification

- 2026-11-02 — front end: `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (24 files, 232
  tests, 100% statements, branches, functions, lines), `npm run build`; `python -I scripts/npm-licenses.py` (21
  shipped packages, 0 outside the allowlist), `python -I scripts/check-vulnerable.py` (0).
- 2026-11-02 — .NET and repo: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c
  Release -p:SkipFrontendBuild=true` (0 warnings), `dotnet test` 1658/1658 (two of four local runs had
  `SidecarProtocolTests.AChildThatHangsTimesOutWithItsLastStderrLinesAndIsKilledOnDispose` fail on its 3 s timing
  while other sessions built; no C# changed here), `changelog.py --check`, `check-fixtures.py --self-test` and
  `check-fixtures.py` (0 findings).
- 2026-11-02 — the mock (Playwright, Chromium, 1280 px), dev server and built `dist/` alike: light, dark and
  `forcedColors: 'active'` in both. Write card with "Grant" and "FullAccess" selected and "Revoke" focused by Tab:
  the focus is a 2 px solid outline at offset 2 (light oklch(0.48 0.1 245), dark oklch(0.8 0.09 240), forced the
  system's highlight color); the selected choice is the accent fill plus a check (forced: Highlight fill,
  HighlightText text and check); the disabled "See the plan" a muted outline with no opacity (forced: GrayText).
  Plan (green `add`), confirm (native checkbox dark in dark), partial result (red and green icons, amber notice),
  the diff (green `added`, red `removed`), a partial user lookup (amber `partial`), the signed-out card (amber).
  Contrast of every visible text run over its painted background, the page with those states: light 187 runs, lowest
  5.45:1; dark 187, lowest 5.86:1; signed out 65 each, 0 below 4.5:1 (a script by hand: loading axe-core from cdnjs
  was not permitted in this session).
- 2026-11-02 — `dark:` classes left: only in `src/components/ui/` (button, badge, input), all reached now: the variant
  is the media query, and dark's outline button computes `bg-input/30` (oklab(0.52 0 0 / 0.3)). No `amber-`,
  `emerald-` or other fixed hue is left in `src/`.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
