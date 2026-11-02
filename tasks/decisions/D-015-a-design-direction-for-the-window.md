---
status: proposed
date: 2026-11-02
cost: medium
sources: [done/018-task-card-design-sketch-for-the-support-team.md, backlog/123-status-color-tokens-one-dark-palette-forced-colors-and-focus-contrast.md, D-009-winforms-webview2-window-host.md]
---

# D-015 — The window follows one design direction, a Windows-native operations console

## Context

The UX/UI review of 2026-11-02 (four reviewers, one through the frontend-design lens) found the window to be
stock shadcn/ui with no point of view on top: the Geist web font, the zinc tokens, rounded cards with a faint
ring, the default type scale, seven equal cards and one badge style for every kind of status. The review's verdict
was that the gap is meaning rather than decoration: nothing separates reading from changing the tenant, success
from neutral, or the current answer from a stale one. The status tokens, the theming and the contrast fixes
(task 123) are needed whatever the direction is; the typography, the surfaces and the one bold moment are the
part that is a taste decision, and the maintainer wanted it decided before any such work is filed.

## Decision

Proposed: the window adopts a "Windows-native operations console" direction. Segoe UI Variable, the system font,
replaces Geist so the page matches the WinForms chrome and ships no web font; Cascadia Mono or Consolas is used
only for real identifiers (object ids, addresses, the plan record); a 20 / 15 / 13 px scale with muted table
headers; a tinted page in light (surface white on a light grey page) with the dark counterparts; one steel-blue
accent for selection and focus; the status triad (green, amber, red) as tokens; 6 px radii and 32 px table rows.
The boldness goes to one place, the plan and confirm step, rendered as a "change ticket": a tinted surface with an
amber left rule, before → after rows with the after value strong, and a confirm button colored by the action.
Everything else stays quiet. The decision is `proposed` until the maintainer decides it; the implementing task is
filed only then.

## Not chosen

- **Stay on the shadcn defaults**: no design work beyond task 123 — the window keeps reading as a template, and
  the write cards keep the weight of a lookup.
- **A lighter touch, tokens and scale only**: the status tokens of task 123 plus the 20 / 15 / 13 scale, Geist kept,
  no change-ticket surface — cheaper, but the plan and confirm step, the one moment the tool exists for, stays a
  form submit.
