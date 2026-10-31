---
size: S
origin: wip/085-signed-exes-and-the-release-zip.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
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

1. [x] The pin in the Core (the thumbprint D-012 names) and the rule: `pass` only for the pinned signer, `warn` with
       the signer for any other valid signature, the existing `warn`/`fail` cases unchanged
2. [x] A test seam for the pin so the Pester tests of `tests/release` sign with a throwaway certificate and see both
       `warn` (not pinned) and `pass` (pinned through the seam) over the built doctor
3. [x] Unit tests of the Core's rule; the coverage gate holds (the interop stays excluded as task 085 listed it)
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session
- 2026-10-31 — the pin: D-012 names no thumbprint, the release notes of `v0.1.0` do (`CN=m365-aegis release signing`,
  `E2F4F13409FB5B3BA437238F0D69EFAF37403619`, read with `gh release view v0.1.0`); it is
  `Authenticode.ReleaseThumbprint` in the Core, its doc comment naming D-012 and the release notes as the second
  place. `WinVerifyTrust`'s signer now gives its certificate's thumbprint too (`FileSignature.Thumbprint`).
- 2026-10-31 — the rule (`Doctor.CheckSignature`): broken, unsigned and untrusted come first as before; then a file
  whose thumbprint is not the pin is `warn` with the signer and its thumbprint and the pin's thumbprint ("not the
  release's certificate"); `pass` only when every file carries the pin. The untrusted warning keeps its detail, and
  its hint asks an administrator to trust the certificate only when the signer is the pinned one; otherwise it says
  the signer is not the release's and to unzip the release again (trusting it would make the wrong signer pass).
- 2026-10-31 — the seam: `M365AEGIS_TEST_RELEASE_THUMBPRINT` (`Authenticode.PinOverrideVariable`, read into
  `DoctorEnvironment.PinOverride`, spaces and case ignored) replaces the pin; the pass then says "the certificate
  M365AEGIS_TEST_RELEASE_THUMBPRINT pins", so the override is never silent.
- 2026-10-31 — `tests/release`: the existing doctor test checks the untrusted hint for a signer that is not pinned and,
  with the variable set, the hint to trust it; a new `Describe` puts the throwaway certificate into
  `LocalMachine\Root` for its two tests (removed in its `AfterAll`) and runs the built doctor over a folder it signed:
  `warn` naming the signer and its thumbprint, and `pass` with the variable. It needs an elevated session, which the
  `windows` job's runner is and this machine's shell is not, so it is skipped here and runs in CI.
- 2026-10-31 — merged in #121 (squash); closed on `main` by the main session. The follow-up filed from the pull
  request: `release.ps1 -RequireSignature` refusing a certificate that is not the pinned one as task 095

## Deviations from the plan

- The untrusted warning's hint changed for a signer that is not the pinned one (the detail stays as task 085 wrote it).
- The `pass` and `warn` over a trusted throwaway certificate run only in an elevated session (the CI runner); a
  developer's shell skips the two.

## Verification

- 2026-10-31 — `dotnet test -c Release`: 1455 passed, the coverage gate 100% lines and branches (Core 5226/5226 lines,
  2895/2895 branches; Cli 1894/1894, 468/468). `Invoke-Pester tests/release` (Pester 5.7.1, not elevated): 7 passed,
  2 skipped (the trusted pair, which needs the local machine's root store); the CI run of the pull request runs them.
  `scripts/release.ps1 -DryRun` signed with a throwaway certificate: exit 0. PSScriptAnalyzer over the four folders
  of the `sidecar` job: 0 findings.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
