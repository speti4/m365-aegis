---
status: idea
size: S
origin: done/052-group-membership-and-owner-writes.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# The Graph `$ref` writes twenty to a `$batch` call

What: the apply of `group members add|remove` and `group owners add|remove` sends its `$ref` POST and DELETE
requests twenty to a `$batch` call (the Graph client's batching of task 003), each change's result read from its
part of the batch answer; a CSV of 100 members is 5 requests instead of 100.
Why: a bulk change (task 054) is one request per row today, which is slow and counts against throttling.
Done when: a plan of 100 member changes applies from recorded batch answers in 5 requests, a failed part reports
its own change and leaves the others applied, and the per-change results are the same as with single requests.
