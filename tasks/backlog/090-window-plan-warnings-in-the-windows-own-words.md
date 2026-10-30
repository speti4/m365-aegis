---
status: idea
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
milestone: M4 Window
---

# The window's plan warnings in the window's own words, not the command line's flags

What: a plan warning shown on a card is worded for the window: the forwarding card's "(--keep-copy keeps one)" becomes a
sentence about the "Keep a copy" box, and every other warning the `Plan` carries is checked for a flag name; the
command line's wording stays as it is, the window maps or rewords it at the card.
Why: the support colleague never sees the command line (the M4 done-when), and a flag name on a card is a dead end.
Done when: no card of the window renders a `--` flag over the fake module and the mock transport, a Vitest test holds
it over the bridge samples, and the command line's plan text is unchanged (its snapshots untouched).
