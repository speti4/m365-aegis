---
status: ready
size: M
origin: session 2026-10-31
created: 2026-10-31
milestone: M6 Release and team
---

# A user-facing README and a setup guide for another tenant's administrator

What: the README becomes a landing page (pitch, badges, what it does with one sample, install, set up, use, a
documentation table, status, license) with no task numbers, milestones or measurements; a new `docs/setup.md` walks an
administrator of any tenant from the app registration (public client, the broker redirect, the one canonical permissions
table with the roles, admin consent, the message-trace service principal) to a working `whoami`, and says what the
`signature` row and SmartScreen mean outside the maintainer's team (D-012 unchanged); `docs/usage.md` keeps its path and
becomes the user's guide; the four "no release yet" sentences and the issue template's wrong advisory link are fixed.
Why: the repo is public with release 0.1.0 out, and the audit of 2026-10-31 found every onboarding fact present but
scattered over a contributor doc, the README and the usage guide, with no admin side in the shipped guide.
Done when: `UsageGuideTests` pass, the fixture scan passes, and a reader with only the README and `docs/setup.md` can
register the app, consent, sign in and run a lookup in their own tenant.
