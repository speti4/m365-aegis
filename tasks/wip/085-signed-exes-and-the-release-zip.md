---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 085 — Signed exes and the release zip through a tag-driven release workflow

## Context

A release workflow on a `v*` tag that publishes the two exes framework-dependent and ReadyToRun (D-007), signs
them (the certificate question is part of the task: a self-signed certificate the team trusts versus a bought one,
recorded as a decision), zips them with the usage guide of task 086 and the licenses, and attaches the zip to a GitHub
release with its hash.
Why: the M6 done-when is the support team running a release from the zip on their own machines; today a release is a
local publish.
Done when: a tag produces a release with a signed zip whose `signtool verify` passes on a clean Windows 11 machine,
the doctor runs from the unzipped folder, and the release steps are in docs/development.md.
What exists already (docs/development.md, "The release"): `scripts/release.ps1` publishes, checks the version, writes
the third-party notices, copies the README as `GUIDE.md`, zips, writes `SHA256SUMS.txt` and the notes; the `release`
job runs it on a `v*` tag on `main`, attests the build provenance and publishes the GitHub release. So this task is the
signing (the certificate decision, the `signtool` step in the script and the job, the verification), the guide swap
(task 086's `docs/usage.md` ships as the guide instead of the README) and the docs; the workflow itself is not rebuilt.
Ninth round of 2026-10-30, with task 086 on one branch; the task session owns the steps below and may reshape them.
The certificate decision is written as a `proposed` decision in the task's PR text and Log; the main session files it
as a `D-` record (task sessions create no ids), and the maintainer decides it.

## Docs that move

- `docs/development.md`, "The release": the signing step, where the certificate lives (a GitHub secret, never the
  repo), how a reader verifies the exes and the zip, the guide that ships
- `README.md`: the release section, if it names the guide file or the verification
- `SECURITY.md`: how a downloaded release is verified (the attestation and the signature)

## Not in this change

- The usage guide's text (task 086, same branch)
- Distribution through winget or Intune
- A bought certificate's purchase (the decision names the options; the maintainer buys or trusts)

## Steps

1. [ ] The certificate decision drafted: self-signed (made once, its public part in the repo for the team to trust,
       the private key a GitHub secret) versus a bought code-signing certificate versus Azure Trusted Signing;
       cost, what the team's machines show, what the doctor can check; the draft in the PR text and the Log
2. [ ] `scripts/release.ps1` signs `m365-aegis.exe` and `m365-aegis-gui.exe` (and the ReadyToRun assemblies the
       chosen scheme covers) with `signtool` when a certificate is given, times tamps them, and verifies them
       (`signtool verify /pa`); without a certificate it says so and the zip is unsigned (the dry run of the
       `windows` job stays green)
3. [ ] The `release` job gets the certificate from a secret, signs, and the release notes name the signature; a
       missing secret fails the release, not the dry run
4. [ ] The doctor's check list, if it can: the running exe's signature state (`pass` signed by the expected
       publisher, `warn` unsigned), or a documented reason why not
5. [ ] Tests: the release script's dry run covers the signing branch over a throwaway self-signed certificate made in
       the test (Pester or a PowerShell test the `windows` job runs); the docs under *Docs that move*

## Log

- 2026-10-30 — promoted on `task/085-signed-release` with task 086

## Deviations from the plan

- none yet

## Verification

- <dated, concrete: what was run, what it showed; the tag release itself is the main session's after the merge>

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
