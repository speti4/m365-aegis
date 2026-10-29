---
status: decided
date: 2026-10-29
cost: cheap
sources: [done/004-output-contract-envelope-table-and-exit-codes.md, backlog/020-decide-on-verify-33-and-the-maintenance-fee.md]
---

# D-008 — Stay on Verify 32.x under MIT; no Open Source Maintenance Fee declaration

## Context

Task 004 brought in Verify for the snapshot tests of the output contract. `Directory.Packages.props` first named
33.3.4, but the `Verify` core package from 33 on ships under the Open Source Maintenance Fee EULA
(`<license type="file">OsmfEula.txt</license>` in its nuspec; the `Verify.XunitV3` wrapper itself still says
MIT), and the build fails with SC021 unless a sponsorship or a time-bounded exemption (`OpenSource` or
`SmallRevenue`, renewed within 12 months) is declared in MSBuild. 32.0.1 is the last MIT release with the same
API. Verified against the nuget.org nuspecs on 2026-10-29.

## Decision

The repo stays on the Verify 32.x line: `Verify.XunitV3` pinned to 32.0.1 in `Directory.Packages.props`,
Dependabot ignoring the major in `.github/dependabot.yml`, every license on `licenses/allowed.json` as before.
Verify is a test-only dependency with no runtime reach, and a build that depends on a yearly declaration is a
maintenance debt a one-person repo does not take on for a snapshot library. Revisit when 32.x stops working
with a newer xunit v3 or .NET: then either declare the exemption (a new decision superseding this one) or
replace the library. Decided by the maintainer on 2026-10-29.

## Not chosen

- Declaring the `OpenSource` exemption and moving to 33.3.4: needs a renewal within 12 months, a silent build
  break when forgotten, for no feature the snapshots need.
- Sponsoring: not for a private personal tool with no revenue.
- Replacing Verify now with hand-rolled snapshot assertions: the 32 line works; replacement waits until it does not.
