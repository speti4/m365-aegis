---
status: ready
size: S
origin: wip/109-doctor-per-permission-consent-check.md
created: 2026-11-01
milestone: M6 Release and team
---

# The setup guide's "Sign in once and check it" points the first run at the doctor's consent row

What: the first-run section of `docs/setup.md` names the doctor's `consent` row (task 109) as the check that the
permissions table was consented in full, and leads from its warning to the consent step of the registration section.
Why: the row was written for the administrator who skipped a table row, and the guide is where that administrator
reads; today the guide's check list predates the row.
Done when: the section names the row, a warn in it points back to the table and the consent step, and the Pester-free
docs check (the fixture scan) passes.
