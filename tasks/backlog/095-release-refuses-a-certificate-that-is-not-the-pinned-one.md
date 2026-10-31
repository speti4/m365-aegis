---
status: idea
size: S
origin: wip/093-doctor-signature-row-pins-the-release-certificate.md
created: 2026-10-31
milestone: M6 Release and team
---

# `release.ps1 -RequireSignature` refuses a certificate whose thumbprint is not the pinned one

What: the release script, when it requires a signature, compares the signing certificate's thumbprint with
`Authenticode.ReleaseThumbprint` (the pin of task 093) and fails before the zip is built when they differ; the release
job inherits the check, so a rotated certificate reaches a release only together with its new pin.
Why: a release signed with a rotated certificate whose pin was not updated would warn on every team machine's doctor;
the job can catch it before the tag publishes.
Done when: the dry run with a throwaway certificate fails with the two thumbprints named unless the test seam of task
093 pins the throwaway one, the Pester tests of `tests/release` cover both, and docs/development.md's rotation steps
name the order (pin first, release second).
