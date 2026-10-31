---
status: idea
size: M
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
milestone: M4 Window
---

# A user picker shared by the User access and the Mailbox access cards

What: a picker component that suggests users by name or address as the colleague types (the user lookup's read over
the bridge), used by the User access card (task 102) and the Mailbox access card instead of their plain text fields;
the picked user's address goes to the bridge request.
Why: task 077 left no reusable picker, so both cards ask for an address typed in full; the support colleague knows
names, not user principal names.
Done when: over the mock transport typing three letters lists matching users, picking one fills the card's request, a
Vitest test holds it over a bridge sample, and the usage guide's card paragraphs describe the picker.
