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

1. [ ] `scripts/release.ps1 -RequireSignature`: the signing certificate's thumbprint read and compared with the pin in
       the built Core (`Authenticode.ReleaseThumbprint`, or the test seam of task 093), the failure before the zip with
       both thumbprints named
2. [ ] The Pester tests of `tests/release`: a throwaway certificate fails unless the seam pins it, and passes with it;
       the signed dry run of the local matrix still passes
3. [ ] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/109-doctor-consent-and-release-pin` by the main session

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: the failing dry run's message, the Pester run, the passing dry run with the seam>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
