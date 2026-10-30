---
size: S
origin: wip/085-signed-exes-and-the-release-zip.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 093 — The doctor's `signature` row pins the release certificate's thumbprint

## Context

The `signature` check of task 085 passes only when the product's files are signed by the certificate D-012 names (its
thumbprint pinned in the Core, with the release notes as the second place it is written), and warns with the signer
when a valid signature comes from another certificate; a rotated certificate is a change of the pin and a release.
Why: today any trusted signer passes the row, so a machine that trusts a wrong certificate would see `pass` on a
replaced exe.
Done when: a file signed by a throwaway certificate the test machine trusts is `warn` naming the signer, a file signed
by the pinned certificate is `pass`, and the Pester tests of `tests/release` cover both over the built doctor.
Tenth round of 2026-10-31, on one branch with tasks 094 and 088; the task session owns the steps below and may reshape
them. The pinned thumbprint is the one D-012 and the release notes of the signed release carry; the task session reads
it from the repo's own record (D-012, `docs/development.md`, the release workflow), never from a certificate file, and
the Pester test that needs the pinned certificate's `pass` uses a test seam (an override of the pin for the throwaway
certificate) rather than the real PFX, which is not on a developer machine.

## Docs that move

- `docs/development.md`: the `signature` row's rule, where the pin lives and how a rotation changes it
- `SECURITY.md`: verifying a release now includes the thumbprint the doctor pins
- `docs/usage.md`: the `signature` line of the doctor section

## Not in this change

- A rotation of the certificate or a release
- The `signtool verify` of `scripts/sign-release.ps1` (it verifies against the signing certificate already)

## Steps

1. [ ] The pin in the Core (the thumbprint D-012 names) and the rule: `pass` only for the pinned signer, `warn` with
       the signer for any other valid signature, the existing `warn`/`fail` cases unchanged
2. [ ] A test seam for the pin so the Pester tests of `tests/release` sign with a throwaway certificate and see both
       `warn` (not pinned) and `pass` (pinned through the seam) over the built doctor
3. [ ] Unit tests of the Core's rule; the coverage gate holds (the interop stays excluded as task 085 listed it)
4. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed, before/after where a number changed>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
