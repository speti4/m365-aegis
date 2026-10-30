---
status: idea
size: S
origin: done/056-window-mailbox-list-through-the-admin-api.md
created: 2026-10-30
milestone: M4 Window
---

# The window's `mailbox-access` over the layers

What: the window's `mailbox-access` request answers the way the command line's `mailbox access` does since task 042:
the mailbox and its Send on Behalf delegates with their display names from the Admin API, FullAccess and Send As from
the warm sidecar session, side by side over the one Exchange token the window holds since task 056.
Why: parity with the command line; today the window's card reads everything through the sidecar and shows the
delegates without their names.
Done when: the window's access card shows the same rows as `mailbox access <mailbox>`, the `window:` log line names
both layers, and the bridge tests hold it over the fixtures and the fake module.
