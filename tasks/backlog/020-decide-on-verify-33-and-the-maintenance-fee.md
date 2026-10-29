---
status: blocked
needs: the maintainer's decision on the Verify license (sponsor, exemption, or stay on 32)
size: S
origin: done/004-output-contract-envelope-table-and-exit-codes.md
created: 2026-10-29
milestone: M1 Foundation
---

# Decide on Verify 33 and the Open Source Maintenance Fee, then move or keep the pin

What: task 004 found that `Verify.XunitV3` 33.x ships under the Open Source Maintenance Fee EULA, not MIT: its
nuspec names `OsmfEula.txt` and the build fails with SC021 unless a sponsorship or a time-bounded exemption
(`OpenSource` or `SmallRevenue`, renewed within 12 months) is declared in MSBuild. The pin stays on 32.0.1
(the last MIT release, same API) and `.github/dependabot.yml` ignores the major. Decide: sponsor, claim an
exemption (and record the yearly renewal), or stay on 32 for good; then apply it (the pin, the ignore rule, the
reason comment in `Directory.Packages.props`, `licenses/allowed.json` if a new license id appears).
Why: a build that depends on a yearly declaration is a maintenance debt the repo should take knowingly or not at
all; the research of 2026-10-28 picked Verify for the snapshots and did not see the license change.
Done when: the decision is a `decisions/` file and the pin, the ignore rule and the license allowlist match it.
