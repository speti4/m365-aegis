---
status: idea
size: S
origin: wip/123-status-color-tokens-one-dark-palette-forced-colors-and-focus-contrast.md
created: 2026-11-02
milestone: M6 Release and team
---

# The hanging-sidecar test independent of the machine's load

What: `SidecarProtocolTests.AChildThatHangsTimesOutWithItsLastStderrLinesAndIsKilledOnDispose`
(`tests/M365Aegis.Core.Tests/Exchange/SidecarProtocolTests.cs`) starts a scripted child that writes 25 stderr lines
and hangs, lets the invoke time out after 3 s and asserts the last 20 lines in the message. On a loaded machine the
child has not written all 25 lines when the timeout fires, so the tail assertion fails. The test should wait for the
25th line (or the scripted child should signal that it wrote them) before the timeout can fire, or inject the clock.
Why: it failed 2 of 4 local runs while other sessions were building (task 123's session, 2026-11-02) and once
under the coverage collector in task 111's session; it has always passed in CI so far, which is where it will
flake next.
Done when: ten consecutive `dotnet test` runs with a parallel build on the machine stay green, and the test no
longer depends on a wall-clock race.
