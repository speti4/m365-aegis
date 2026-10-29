---
status: idea
size: M
origin: done/017-window-hello-bridge-behind-iuihost.md
created: 2026-10-29
milestone: M4 Window
---

# The window signs in: `login` over the bridge, the account picker parented to the window

What: a `login` request over the bridge (and `logout`), so the window's sign-in card offers "Sign in" when the
silent read of task 017 answers signed-out; the broker's account picker is parented to the window's handle
(the command line's `ConsoleWindow` becomes the host's handle through `IUiHost`), the device-code fallback is
shown in the card, and the card refreshes itself when the sign-in completes.
Why: a support colleague runs the window, not the command line (D-001); today the page can only show
signed-out and point at `m365-aegis login`.
Done when: a signed-out window signs in from the card on the maintainer's machine without a console, `whoami`
over the bridge answers signed-in afterwards, and the `--smoke` run still answers no request.
