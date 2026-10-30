---
status: idea
size: S
origin: wip/085-signed-exes-and-the-release-zip.md
created: 2026-10-30
milestone: M6 Release and team
---

# The doctor's `signature` row pins the release certificate's thumbprint

What: the `signature` check of task 085 passes only when the product's files are signed by the certificate D-012 names
(its thumbprint pinned in the Core, with the release notes as the second place it is written), and warns with the
signer when a valid signature comes from another certificate; a rotated certificate is a change of the pin and a
release.
Why: today any trusted signer passes the row, so a machine that trusts a wrong certificate would see `pass` on a
replaced exe.
Done when: a file signed by a throwaway certificate the test machine trusts is `warn` naming the signer, a file signed
by the pinned certificate is `pass`, and the Pester tests of `tests/release` cover both over the built doctor.
