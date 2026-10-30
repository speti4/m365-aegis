---
size: S
origin: wip/067-publish-the-repository-with-a-rewritten-history.md
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 069 — A one-way mirror of the task files into GitHub issues

## Context

Every numbered task and decision under `tasks/` becomes one GitHub issue through the project-pal task standard's
mirror: the file stays the only truth, an issue is never edited by hand, nothing flows back; every `## ` heading
of `tasks/roadmap.md` is a milestone. Task 012 left the issue mirror to M6; with the repository public (D-010) an
outsider reads the plan and its state on the Issues tab instead of browsing the task folder.
Decided 2026-10-30: the mirror runs at the start of the sixth round, in the main session, with the `ci` runner
(the minutes of a public repository are free): `.github/workflows/sync-issues.yml` runs the script on every push
to `main` that touches `tasks/`, and the backfill runs once by hand. Every file's H1 and Context is public issue
text, which the task files are written as since D-010.
Done when: `python -I scripts/sync-issues.py --dry-run` reports every file, the backfill has run once, a second
run reports `created 0, updated 0`, and the tasks README names the mirror.

## Docs that move

- `tasks/README.md`: one preamble line naming the mirror (outside the index markers)
- `docs/development.md`, "CI and releases": the `sync-issues` workflow in the job list
- `CLAUDE.md`, "Tasks": the mirror line of the standard's snippet

## Not in this change

- Any edit of an issue by hand, or any flow from the issues back into the files
- Closing a milestone (a release does that)
- The issue templates and labels of task 012 beyond what the mirror creates

## Steps

1. [x] The script and the workflow from the skill's templates (`scripts/sync-issues.py`, `.github/workflows/sync-issues.yml`),
       the README line, the docs line; on `task/069-issue-mirror` in the main checkout (no worktree: a settings task)
2. [x] `python -I scripts/sync-issues.py --dry-run`: every file reported, no `skip`, no error
3. [x] The backfill on the maintainer's go; a second run reports `created 0, updated 0`
4. [x] Pull request, CI green (`workflow-lint` over the new workflow), squash merge; the workflow's own run on the
       push reports nothing to change

## Log

- 2026-10-30 — promoted by the main session at the start of the sixth round (063–075)
- 2026-10-30 — `scripts/sync-issues.py` was already in the repo as an older copy of the template (the changelog
  generator reads the task files through its parser, task 067's docs said so); replaced by the template's version 3
  unchanged (`--version` prints `3`; the names `changelog.py` imports — `TASKS`, `TASK_FILE`, `parse_frontmatter`,
  `scalar`, `_heading`, `_trim`, `load_roadmap` — are all there, `changelog.py --check` green). The workflow is the
  template's `sync-issues.yml` as it comes (`branches: [main]`, `paths: ["tasks/**", "scripts/sync-issues.py"]`,
  `issues: write` on the job only, the checkout pinned by SHA); `uvx zizmor .github`: no findings. The README
  preamble line, the CLAUDE.md line, `docs/development.md` ("CI and releases" names the workflow, a new "The issue
  mirror" section, the changelog paragraph's "although the mirror is not set up" dropped).
- 2026-10-30 — `python -I scripts/sync-issues.py --dry-run`: `planned (dry run): created 85, updated 0, unchanged 0,
  duplicates closed 0; 85 items (75 tasks, 10 decisions)`, no `skip` line, exit 0.
- 2026-10-30 — pull request #1 opened from the branch (the first pull request of the public repository); the
  backfill ran from the main checkout on the maintainer's go: `applied: created 85, updated 0, unchanged 0,
  duplicates closed 0`; the second run: `applied: created 0, updated 0, unchanged 85, duplicates closed 0`.

## Deviations from the plan

- The script was not installed, it was upgraded: an older template copy was already in the repo for the changelog
  generator's parser, so the change is a version bump of a file the repo had, not a new file.
- The backfill ran before the pull request merged (the workflow's own run after the merge is the proof of
  idempotence in CI; by hand it was already proven by the second run).

## Verification

- 2026-10-30 — `python -I scripts/sync-issues.py --dry-run` 85 to create, no skip; the backfill created 85
  (75 tasks, 10 decisions) with the labels and the six milestones of the roadmap; the second run created 0,
  updated 0, unchanged 85; `python -I scripts/changelog.py --check` and `python -I scripts/check-fixtures.py`
  green with the new script; `uvx zizmor .github` no findings over the new workflow.
- 2026-10-30 — pull request #1 green on its first run (all ten jobs, the `windows` one included) and on the
  docs-only rerun; squash-merged as 2f0c252; the `sync-issues` workflow's run on that commit (41087800556) ended
  `applied: created 0, updated 0, unchanged 85, duplicates closed 0`.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-30`
