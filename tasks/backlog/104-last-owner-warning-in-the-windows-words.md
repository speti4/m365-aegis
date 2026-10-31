---
status: idea
size: S
origin: wip/090-window-plan-warnings-in-the-windows-own-words.md
created: 2026-10-31
milestone: M4 Window
---

# The group writes' last-owner warning in the window's words when a group write card arrives

What: the warning the group owner writes carry when a change leaves a group without an owner names a command (`group
owners-missing`); the window's wording map of task 090 rewords it at the card to what the colleague can do in the window,
and the Vitest test that forbids a `--` flag on a card also forbids a command name.
Why: a command name on a card is the same dead end as a flag for the colleague who never sees the command line (the M4
done-when); task 090 reworded the flags and left the command names for the card that will show them.
Done when: with a group write card over the mock transport no card renders a `--` flag or a `m365-aegis` command name,
the test holds it over the bridge samples, and the command line's text is unchanged.
