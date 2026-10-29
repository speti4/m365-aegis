---
size: S
origin: done/004-output-contract-envelope-table-and-exit-codes.md
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 020 — Decide on Verify 33 and the Open Source Maintenance Fee, then move or keep the pin

## Context

Task 004 found that `Verify.XunitV3` 33.x depends on a `Verify` core under the Open Source Maintenance Fee EULA,
not MIT: the build fails with SC021 unless a sponsorship or a time-bounded exemption is declared in MSBuild. The
pin stayed on 32.0.1 (the last MIT release, same API) and `.github/dependabot.yml` ignores the major. The
decision was the maintainer's: sponsor, claim an exemption with a yearly renewal, or stay on 32. Decided
2026-10-29: stay on 32 (D-008); the pin and the ignore rule already match, so this task records the decision
and points the two comments at it.

## Docs that move

- `tasks/decisions/D-008-stay-on-verify-32-under-mit.md`: new, decided.
- `Directory.Packages.props` and `.github/dependabot.yml`: the reason comments name D-008.

## Not in this change

- Any package version change: 32.0.1 stays; `licenses/allowed.json` is untouched (MIT already listed).
- A replacement of Verify (only when 32.x stops working, per D-008).

## Steps

1. [x] The claim verified against nuget.org: `Verify` 33.3.4 nuspec `license type="file" OsmfEula.txt`; `Verify.XunitV3`
       33.3.4 MIT but depends on `Verify` 33.3.4; `Verify` 32.0.1 MIT
2. [x] D-008 written as decided (the maintainer, 2026-10-29)
3. [x] The comments in `Directory.Packages.props` and `.github/dependabot.yml` point at D-008
4. [x] Verification: the pin is 32.0.1, the ignore rule is in place, `tasks_check` green

## Log

- 2026-10-29 — nuspecs fetched from `api.nuget.org/v3-flatcontainer`: the core `Verify` 33.3.4 carries the
  EULA, the wrapper does not; promoted, decided and closed in the main session in one round

## Deviations from the plan

- none

## Verification

- 2026-10-29 — `Directory.Packages.props` pins `Verify.XunitV3` 32.0.1; `.github/dependabot.yml` ignores its
  semver-major; D-008 exists with `status: decided`; `tasks_check` 0 errors; `python -I scripts/changelog.py
  --check` green after the regeneration

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log (none)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-29`
