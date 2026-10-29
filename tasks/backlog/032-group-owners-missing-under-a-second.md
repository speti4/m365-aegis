---
status: ready
size: S
origin: done/005-group-teams-and-sharepoint-lookups.md
created: 2026-10-29
milestone: M1 Foundation
---

# `group owners-missing` under a second

What: the live round of 2026-10-29 measured `group owners-missing` at 7.9 s over 178 ownerless groups in three
paged requests (`owners/$count eq 0` as an advanced query); the done-when of task 005 asked for well under a
second plus the network. Try a leaner `$select`, the largest page (`$top=999`), a count-only probe first, or
`$batch` over the pages, and measure again live with the request count and the elapsed time in the Log.
Why: the daily "which groups have no owner" question is the one the admin center answers slowest, and the
command line's promise is the speed.
Done when: the live run answers in well under a second plus the network on the same tenant, or the Log states
the measured floor of the endpoint and the hint names it.
