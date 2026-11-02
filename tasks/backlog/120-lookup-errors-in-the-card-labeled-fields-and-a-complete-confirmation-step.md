---
status: idea
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# Lookup errors shown in the card, labeled fields, Enter and Escape on the write cards, and a complete confirmation step

What: a failed lookup shows its error in the card (the write cards' `Notice` and `ErrorText`), clears or dims the
old answer and marks the field invalid; the toast keeps a close button. A visible label above every field (Mailbox,
Users, Forward to, From, To); the write cards' fields in a form, so Enter means "See the plan" and Escape goes back.
The confirmation shows what the plan showed (action badge, from → to, the warnings, the account it runs as), its
button names the action (grant, revoke, start or stop forwarding; destructive for a removal), plus a Cancel.
Why: the UX/UI review of 2026-11-02 (U-1, D-3, A-4; U-3, D-7, A-8, U-8, A-10; U-5, U-6, C-2, D-2): after a misspelled
name the previous group's members stay on the card as the answer, a filled write card does not say which field is
the mailbox, and the last look before a change shows less than the plan did.
Done when: a keyboard walk of the mock shows the error under the field and no stale table, labels, Enter and Escape
on the write cards, a confirmation that names the action and the account; the tests and usage.md "The window" match.
