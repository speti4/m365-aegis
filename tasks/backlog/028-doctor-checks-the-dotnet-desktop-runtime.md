---
status: idea
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
milestone: M6 Release and team
---

# The doctor checks the .NET Desktop Runtime 10 for the window

What: a `dotnet-desktop-runtime` row in `doctor`: the window (a WinForms form, D-009) needs the .NET Desktop
Runtime 10 next to the .NET Runtime 10 the command line needs; the check reads the installed shared frameworks
(`Microsoft.WindowsDesktop.App`) and names the winget id (`Microsoft.DotNet.DesktopRuntime.10`) in the hint.
Why: a machine with only the base runtime gets the apphost's generic "framework missing" message when the window
starts, and the doctor is what a colleague runs first (task 008).
Done when: the check has a pass, a warn and a fail case in the tests with the framework list faked, and the
README's requirements section points at the doctor.
