---
status: idea
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# The user picker on the write cards

What: the Users field of the mailbox permissions card and the Forward to field of the forwarding card use the
`UserPicker` the access cards already have; a pick is added to the comma-separated list, a typed full address
still passes through. The mailbox field offers suggestions from the `mailbox-list` reply the window already has.
Why: the UX/UI review of 2026-11-02 (finding U-3): the picker exists and works, but the screen where a wrong name
costs most, a change to the tenant, is the one that asks for free text.
Done when: both write cards pick a user from the directory or the snapshot in the mock, the plan is computed from
the picked addresses, and the tests cover a pick and a typed address.
