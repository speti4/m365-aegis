---
status: idea
size: S
origin: wip/117-folder-permissions-domain-read-in-parallel.md
created: 2026-11-01
milestone: M5 Snapshot and views
---

# The take's calendar reads measured at a ceiling of 12 and 16, and a profile setting if the numbers ask for one

What: a live take with the folderPermissions gate's ceiling at 12 and one at 16 (a constant for the measurement, or an
`adminApiParallel` profile setting on the `fullSharingDays` pattern), next to task 117's eight.
Why: eight in flight gave 1.46 times the throughput of four, not twice, with no throttled answer (task 117's live run of
2026-11-01: about 2.0 s per call at eight against 1.45 s at four), so the service may serialize the reads per anchor
mailbox, in which case a higher ceiling buys little and the constant stays.
Done when: both measurements are in the Verification next to the eight, and either the constant stays eight with the
reason written down or the profile setting lands with its docs.
