---
status: ready
size: S
origin: wip/113-window-names-for-the-snapshot-parts-in-the-access-cards-warnings.md
created: 2026-11-01
milestone: M4 Window
---

# A skipped snapshot domain's note in the window's words on the access cards

What: the warning the access cards show for a skipped domain (today OneDrive: "other users' OneDrives need a consent the
tool does not ask for (Sites.Read.All …; task 092)") is reworded through the wording map of tasks 090, 104 and 113 to
what the colleague can act on ("other people's OneDrive files are not in the snapshot; ask the administrator"), and the
Vitest test that forbids a command name on a card also forbids a task number and a permission name.
Why: the take's note is written for the administrator at the command line; a permission name and a task number on a
card are the same dead end as a flag.
Done when: over the bridge samples no card renders a permission name or a `task NNN` reference, the test holds it, and
the command line's note is unchanged.
