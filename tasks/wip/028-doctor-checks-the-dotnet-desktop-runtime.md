---
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M6 Release and team
---

# 028 — The doctor checks the .NET Desktop Runtime 10 for the window

## Context

A `dotnet-desktop-runtime` row in `doctor`: the window (a WinForms form, D-009) needs the .NET Desktop
Runtime 10 next to the .NET Runtime 10 the command line needs; the check reads the installed shared frameworks
(`Microsoft.WindowsDesktop.App`) and names the winget id (`Microsoft.DotNet.DesktopRuntime.10`) in the hint.
Why: a machine with only the base runtime gets the apphost's generic "framework missing" message when the window
starts, and the doctor is what a colleague runs first (task 008).
Done when: the check has a pass, a warn and a fail case in the tests with the framework list faked, and the
README's requirements section points at the doctor.
Part of the Window bundle (017+030, 027, 028, 031, 029) of the round of 2026-10-29; the task session owns the
steps below and may reshape them.

## Docs that move

- `README.md`, requirements: the Desktop Runtime and the doctor
- `docs/development.md`, the doctor: the new row

## Not in this change

- an installer that pulls the runtime (M6)

## Steps

1. [x] The check in `Core/Diagnostics/Doctor.cs` behind a `DoctorEnvironment` seam that lists the installed
       shared frameworks; the winget id in the hint
2. [x] Pass, warn and fail cases in `DoctorTests` with the list faked; the doctor fixtures follow
3. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Window bundle (017+030, 027, 028, 031, 029)
- 2026-10-29 — `DoctorEnvironment.ReadDesktopRuntimeVersions` lists the folders under
  `shared\Microsoft.WindowsDesktop.App` of the installation whose `Microsoft.NETCore.App` runs the process (the
  one the window's apphost resolves, no `dotnet` call). `Doctor.CheckDesktopRuntime`, right after
  `dotnet-runtime`, not required: pass on a Desktop Runtime of the same major version as the .NET Runtime, warn
  when its newest is a patch behind the .NET Runtime (`winget upgrade …`), fail when none of that major is there
  (`winget install Microsoft.DotNet.DesktopRuntime.10`, other majors named in the detail), skipped off Windows.
  The three doctor fixtures carry the new row (the warnings one with the warn), the seven doctor snapshots were
  accepted with the row added and nothing else changed.

## Deviations from the plan

- none

## Verification

- 2026-10-29 — `DoctorTests.TheDesktopRuntimeOfTheWindow`: pass (8.0 + 10.0.4; 10.0.5), warn (10.0.1 against
  10.0.4), fail (8.0 and 9.0 only, with them in the detail; none), and skipped off Windows; `dotnet test` green.
- 2026-10-29 — on this machine `m365-aegis doctor --offline --table`: `dotnet-desktop-runtime  pass  10.0.14`,
  next to `dotnet-runtime  pass  10.0.14`.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
