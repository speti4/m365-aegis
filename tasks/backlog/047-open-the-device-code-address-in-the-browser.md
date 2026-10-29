---
status: idea
size: S
origin: done/040-window-sign-in-over-the-bridge.md
created: 2026-10-30
milestone: M4 Window
---

# An "Open in browser" button for the device-code sign-in address

What: the device-code card shows the sign-in address as text to type, because the window opens no other origin; an
"Open in browser" button hands the address to the system browser through the host (`IUiHost`, a `ShellExecute` of
the URL only, never a navigation of the WebView2), next to the copy button of the code.
Why: typing the address is the slow step of the fallback, and the window must stay closed to other origins.
Done when: the button opens the address in the default browser on the maintainer's machine, the window itself
still refuses other origins, and the browser mock covers the button.
