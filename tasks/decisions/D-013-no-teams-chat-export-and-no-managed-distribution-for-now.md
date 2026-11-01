---
status: decided
date: 2026-11-01
cost: cheap
sources: [D-001-build-a-thin-personal-ops-layer-not-a-product.md, D-003-own-public-client-with-msal-and-wam.md, tasks/roadmap.md]
---

# D-013 — No Teams chat export, and no managed distribution for now

## Context

The roadmap of 2026-10-28 carried two items no task had reached by 2026-11-01: a Teams chat export under M5 (D-001
names it among the gaps the tool fills, and D-003 reserves an app-only certificate profile for it, since
`getAllMessages` is an application-permission protected API), and a distribution through Intune or winget under M6.
With M5's views and M6's release in place, the maintainer reviewed both.

## Decision

- **No Teams chat export.** Reading chat content would put personal information in front of the tool and its user,
  and the maintainer does not take that on from a data-protection standpoint. The item is out of scope, not postponed:
  the tool reads who can reach what, never what was said. The app-only certificate profile of D-003 loses its only
  planned use and is not built until something else needs it.
- **No managed distribution for now.** The signed zip on the releases page, checked by its checksum and its build
  attestation and unzipped by hand, is the distribution; an Intune or winget rollout is not planned at present and
  may be revisited when the team's machines ask for it. The M6 done-when stays: the support team runs a release from
  the zip on their own machines.

Cost: cheap. Nothing built depends on either item; the roadmap and the two decisions carry a note. Decided by the
maintainer on 2026-11-01.

## Not chosen

- A Teams chat export behind an app-only profile and an extra consent: the research of 2026-10-28 counted it among the
  gaps, but the read is content, not access, and the privacy cost outweighs the gap.
- A metadata-only Teams read (chat members, counts, no message text): not asked for by anyone today; a later decision
  if a need appears.
- A winget manifest or an Intune package now: the team is a few people on managed machines where a hand-installed zip
  with a trusted certificate (D-012) is enough; the packaging work would precede any demand for it.
