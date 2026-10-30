---
status: idea
size: M
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# Signed exes and the release zip through a tag-driven release workflow

What: a release workflow on a `v*` tag that publishes the two exes framework-dependent and ReadyToRun (D-007), signs
them (the certificate question is part of the task: a self-signed certificate the team trusts versus a bought one,
recorded as a decision), zips them with the usage guide of task 086 and the licenses, and attaches the zip to a GitHub
release with its hash.
Why: the M6 done-when is the support team running a release from the zip on their own machines; today a release is a
local publish.
Done when: a tag produces a release with a signed zip whose `signtool verify` passes on a clean Windows 11 machine,
the doctor runs from the unzipped folder, and the release steps are in docs/development.md.
