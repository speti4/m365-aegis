---
status: ready
size: L
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# 100% line and branch coverage of the Core, the command line, the front end and the sidecar, enforced in CI

What: coverage measured in every test layer and held at 100% lines and branches by a CI threshold: `coverlet.MTP`
(in `Directory.Packages.props`, referenced by no test project yet) for Core and Cli, `@vitest/coverage-v8` for the
front end, Pester `CodeCoverage` for the sidecar; an in-process entry for the command line and a dot-sourceable
function layer for the sidecar, since both run as child processes today and no collector sees them; the tests that
close the gaps (14 source files no test names, `WriteRunner` to `DeviceCodePrompts`; 4 untested front-end modules).
Why: the maintainer's target of 2026-10-30; a public repo whose writes touch a tenant should show every branch
exercised, and a threshold keeps a new branch from landing untested.
Done when: the three coverage jobs fail CI below 100/100, the reports are run artifacts, and the exclusion list
(the WinForms host and P/Invoke shells that need a display, `[ExcludeFromCodeCoverage]` with a reason each) is in
`docs/development.md`, reviewed by the maintainer.
