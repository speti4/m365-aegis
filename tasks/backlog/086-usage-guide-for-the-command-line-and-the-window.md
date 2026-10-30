---
status: idea
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# A usage guide for the command line and the window, shipped in the release zip

What: `docs/usage.md` for the support team: the sign-in, the daily lookups per domain with examples, the writes with
`--dry-run` and `--yes`, the plan records and a resume, the window's cards, the doctor's checks and what each failure
means; the release zip of task 085 carries it, so the guide is written for a reader who has the zip and no repo.
Why: the README is written for a contributor; the M6 done-when needs a guide a support colleague reads once.
Done when: the guide covers every command of `m365-aegis --help` and every card of the window, every example runs over
the fixtures, and `scripts/check-fixtures.py` passes over it (no tenant data in the examples).
