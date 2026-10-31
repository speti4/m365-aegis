---
status: idea
size: S
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
milestone: M4 Window
---

# The window's own names for the snapshot parts in the access card's warnings

What: the access card's warnings name the snapshot's domains in the window's words ("SharePoint sites", "OneDrive")
instead of the store's identifiers (`sites`, `oneDrive`), through the wording map of tasks 090 and 104.
Why: a domain identifier is the command line's word; the colleague who reads "sites is partial" on a card has no
command line to look it up in.
Done when: no card renders a bare domain identifier in a warning over the bridge samples, the Vitest dictionary test of
task 104 holds the new entries, and the command line's text is unchanged.
