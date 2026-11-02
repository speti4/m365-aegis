---
status: idea
size: M
origin: session 2026-11-02
created: 2026-11-02
milestone: M4 Window
---

# The window's vocabulary: Microsoft's terms, one verb per action from the card to the plan, no raw values as text

What: one glossary for the window. "Full Access" as Microsoft writes it (the CLI still accepts `FullAccess`);
"permission" where the cards say right, grant or access ("Mailbox permissions" / "Change mailbox permissions");
the plan badge and the confirm button use the card's verbs (Grant, Revoke, Forward, Stop forwarding), not the enum
(add, remove, set, clear), and the Core's titles read "Forward the mail of…"; raw values get display names through
shared maps (Shared mailbox; Microsoft 365, Security, Mail-enabled security, Distribution list; User, Group, Device;
Owner; Free/busy only; a snapshot named by its time, not its `.sqlite` file); Choose › Review › Confirm › Result
with "Back to edit"; plan warnings titled "Check before you confirm"; toasts and refusals through `sentence()`, hints
say "enter" not "give"; a `plural()` helper for every "(s)"; sentence-case badges; one-line card descriptions;
people named one way (sign-in name, email address, object ID; a site column is URL).
Why: the UX/UI review of 2026-11-02 (C-1, C-3 to C-11, D-11, U-11, D-10). Done when: components, the Core texts the window surfaces, the tests and usage.md "The window" agree with the glossary and the review's seven usage.md mismatches are gone.
