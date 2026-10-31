---
size: S
origin: wip/093-doctor-signature-row-pins-the-release-certificate.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 095 — `release.ps1 -RequireSignature` refuses a certificate whose thumbprint is not the pinned one

## Context

The release script, when it requires a signature, compares the signing certificate's thumbprint with
`Authenticode.ReleaseThumbprint` (the pin of task 093) and fails before the zip is built when they differ; the release
job inherits the check, so a rotated certificate reaches a release only together with its new pin.
Why: a release signed with a rotated certificate whose pin was not updated would warn on every team machine's doctor;
the job can catch it before the tag publishes.
Done when: the dry run with a throwaway certificate fails with the two thumbprints named unless the test seam of task
093 pins the throwaway one, the Pester tests of `tests/release` cover both, and docs/development.md's rotation steps
name the order (pin first, release second).
Eleventh round of 2026-10-31, on one branch with task 109; the task session owns the steps below and may reshape them.
No live check: the signed dry run with a throwaway certificate is the local matrix's.

## Docs that move

- `docs/development.md`: the certificate rotation steps name the order (pin first, release second) and the check
- `CONTRIBUTING.md`: the release checks paragraph, if the dry run's invocation changes

## Not in this change

- The doctor's signature row (task 093)
- The release workflow's trigger or its secrets

## Steps

1. [x] `scripts/release.ps1 -RequireSignature`: the signing certificate's thumbprint read and compared with the pin in
       the built Core (`Authenticode.ReleaseThumbprint`, or the test seam of task 093), the failure before the zip with
       both thumbprints named
2. [x] The Pester tests of `tests/release`: a throwaway certificate fails unless the seam pins it, and passes with it;
       the signed dry run of the local matrix still passes
3. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/109-doctor-consent-and-release-pin` by the main session
- 2026-10-31 — `scripts/release.ps1`: under `-RequireSignature`, after the certificate file is found and before the
  timestamp check, the release notes and the publish, the PFX is opened (the password of
  `M365AEGIS_SIGNING_PASSWORD`, as `sign-release.ps1` opens it) and its thumbprint compared with the
  `ReleaseThumbprint` constant of `src/M365Aegis.Core/Diagnostics/Authenticode.cs`; a mismatch throws with both
  thumbprints and the order (pin first, release second). `M365AEGIS_TEST_RELEASE_THUMBPRINT` replaces the pin as in the
  doctor (spaces and case ignored, the message then names the variable), and is refused together with `-Tag`, so the
  release job is always held to the pin in the code. The pin check sits before the timestamp check, which lets the
  tests prove the pass without a network or a publish: past the pin, the empty `-TimestampUrl` is the next refusal.
- 2026-10-31 — `tests/release/sign-release.Tests.ps1`: five tests in the `release.ps1 -RequireSignature` block (no
  certificate; the throwaway certificate refused with both thumbprints; the seam pins it and the run goes on to the
  timestamp check; another thumbprint in the seam refused; the seam with `-Tag` refused); the former "without a
  timestamp server" test is the seam's pass now. `docs/development.md`: the `-RequireSignature` sentence, the
  signing tests' list, the rotation steps (pin, secrets, tag) and the doctor section's pointer to them.

## Deviations from the plan

- The pin is read from the Core's source (`Authenticode.cs`), not from the built `M365Aegis.Core.dll`: the source is
  what the same run's publish compiles, reading it lets the check fail before anything is built, and it needs no
  assembly load (which would lock the file the signing step rewrites) nor a metadata reader in the script.
- `CONTRIBUTING.md` is unchanged: the dry run's invocation did not change.

## Verification

- 2026-10-31 — `release.ps1 -DryRun -RequireSignature -CertificatePath <throwaway pfx>` refuses before the release
  notes: `the signing certificate's thumbprint <the throwaway one> is not the one the Core pins
  (Authenticode.ReleaseThumbprint), <the pin>: a new certificate is pinned in the Core first, in a change of its own,
  and released after it (docs/development.md, The release)`.
- 2026-10-31 — `Invoke-Pester -Path tests/release` (Pester 5.7.1, signtool of Windows SDK 10.0.22621.0): 10 passed,
  0 failed, 2 skipped (the elevated pair, a shell that is not elevated); the five `-RequireSignature` tests among the
  passed (the seam's pass goes on to the timestamp refusal). PSScriptAnalyzer over `scripts` and `tests/release`: 0
  findings.
- 2026-10-31 — the signed dry run of the local matrix (`release.ps1 -DryRun -CertificatePath <throwaway pfx>
  -TimestampUrl ''`, no `-RequireSignature`, as the `windows` job runs it): exit 0, five files signed and verified
  against the certificate, the zip 6.3 MB.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change (`CONTRIBUTING.md` not needed: the invocation is unchanged)
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
