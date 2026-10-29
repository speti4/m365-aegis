---
status: idea
size: M
origin: done/016-mailbox-access-reverse-lookup-and-forwarders.md
created: 2026-10-29
milestone: M2 Exchange
---

# `mailbox access` expands a grant made to a group into the group's members

What: when FullAccess, Send As or Send on Behalf is held by a group, `mailbox access <mailbox>` lists the group's
members (through `group members`, transitive) under the group's row, and `mailbox access --user <upn>` finds the
mailboxes a user reaches through a group; the expansion is a switch (`--expand-groups`) so the plain answer stays
one sidecar round.
Why: the lookups of task 016 list the group, not who is in it, and "who can open this mailbox" means the people.
Done when: a grant to a group answers with the members from the fake module and the Graph fixtures, and live on a
shared mailbox whose access is granted to a group.
