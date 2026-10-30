---
status: idea
size: S
origin: done/071-write-guard-test-over-the-whole-command-tree.md
created: 2026-10-30
milestone: M4 Window
---

# A write-guard test over the window's bridge writes

What: a test that enumerates the bridge's write requests from the protocol itself (the way task 071 walks the command
tree) and drives each one without a confirmation, asserting that the fixture transport recorded no request other than
GET, the fake module no write cmdlet and the plans folder no record.
Why: SECURITY.md names a write without a confirmation as a vulnerability; task 071 holds it for the command line only,
and the window's plan-and-confirm cards (M4) will add write requests to the bridge.
Done when: the test is green over the bridge's write requests, a new write request joins it without an edit, and it is
part of the M4 write card's done-when.
