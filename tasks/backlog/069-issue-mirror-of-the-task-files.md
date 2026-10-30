---
status: idea
size: S
origin: wip/067-publish-the-repository-with-a-rewritten-history.md
created: 2026-10-30
milestone: M6 Release and team
---

# A one-way mirror of the task files into GitHub issues

What: every numbered task and decision under `tasks/` becomes one GitHub issue through the project-pal task
standard's mirror (`/tasks mirror`): the file stays the only truth, an issue is never edited by hand, and the
`ci` runner is the fit for a public repository (the minutes are free).
Why: task 012 left the issue mirror to M6; with the repository public, an outsider reads the plan and its state
on the Issues tab, with milestones from `tasks/roadmap.md`, instead of browsing the task folder.
Done when: `python -I scripts/sync-issues.py --dry-run` reports every file, the backfill has run once, a second run
reports `created 0, updated 0`, and the tasks README names the mirror.
