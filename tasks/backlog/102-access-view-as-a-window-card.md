---
status: idea
size: M
origin: wip/084-access-view-for-one-user-from-the-snapshot.md
created: 2026-10-31
milestone: M5 Snapshot and views
---

# The access view as a window card

What: a card of the window that asks for a user (by name or address, the user lookup's picker) and shows what `access
<user>` of task 084 answers from the newest snapshot: the groups with their via groups, the sites, the shared items,
the mailboxes and calendars, the counts of what everyone reaches, the snapshot's age and its warnings; a bridge request
over the same Core read, no live call.
Why: "who can reach what" is the support team's question, and the support colleague works in the window, not the
command line (the M4 and M5 done-whens).
Done when: over the mock transport the card answers for a fixture user with the snapshot's date, says so plainly when no
snapshot exists, a Vitest test holds it over a bridge sample, and the usage guide has the card.
