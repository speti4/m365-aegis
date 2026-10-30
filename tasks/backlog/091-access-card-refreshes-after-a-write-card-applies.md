---
status: idea
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
milestone: M4 Window
---

# The mailbox access card refreshes after a write card applies a change to the mailbox it shows

What: when a write card's apply finishes (task 082's `plan-applied`) for a mailbox the mailbox access card is showing,
the access card reads the mailbox again and marks the refresh; a failed or partial apply refreshes too, so the card
shows what the service holds now.
Why: after a grant the access card shows the old list until the colleague asks again, which looks like a grant that did
not happen; the browser mock's apply changes nothing, so the stale state is visible there too.
Done when: over the fake module an apply on the shown mailbox is followed by one `mailbox-access` request and the new
grant on the card, an apply on another mailbox sends none, and the mock transport's apply changes its state so the
browser shows the refresh.
