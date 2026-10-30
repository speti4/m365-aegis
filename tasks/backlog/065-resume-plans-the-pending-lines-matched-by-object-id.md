---
status: idea
size: S
origin: done/062-resume-a-half-applied-batch-from-its-record.md
created: 2026-10-30
milestone: M3 Writes with safety
---

# A resume plans only the pending lines and matches them by object id

What: `--resume <record>` (task 062) plans only the lines of the batch whose change the record left failed or skipped
(today it plans the whole batch again and keeps the pending part), and matches a pending change to the new plan by
the target's object id kept in the record instead of the rendered text (today a rename between the apply and the
resume, or a record edited by hand, shows as a warning instead of a match).
Why: a 100-line batch with 60 lines left reads 100 objects today; and the text match is brittle, as the live check of
2026-10-30 showed with an encoding slip.
Done when: a record of 100 lines with 60 pending resumes with 60 reads, a renamed target still matches, and the
record format carries the ids with a version bump the old records still read under.
