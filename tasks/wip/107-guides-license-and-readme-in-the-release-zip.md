---
size: S
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 107 — The setup and usage guides, the license and a readme in the release zip

## Context

The guides live in `docs/` on GitHub first (decided 2026-10-31, task 106 writes `docs/setup.md`), and the release zip
must carry the same files, so a colleague with only the download has the administrator's steps too. Today
`scripts/release.ps1` copies `docs/usage.md` as `GUIDE.md` and nothing else from `docs/`; the zip has no license file
and no readme.
Done when: a local `release.ps1` run without a certificate produces a zip whose file list has `GUIDE.md`, `SETUP.md`,
`LICENSE.txt` and `README.txt`, and the tests and lint checks pass.

## Docs that move

- `scripts/release.ps1`: copies `docs/setup.md` → `SETUP.md`, `LICENSE` → `LICENSE.txt`, writes `README.txt` (what the
  folder holds, the guides, the GitHub URL, how to verify the download); copies `docs/images/` when it exists
- `scripts/changelog.py` (`--notes`): the footer names the guides in the zip and the docs on GitHub
- `docs/development.md`, "The release": the zip's contents
- `SECURITY.md`: the zip's contents where it describes the download
- the test that covers the zip's file list, if one exists under `tests/` or `scripts/` (extend it; otherwise say so)

## Not in this change

- the guides' text (task 106); until 106 merges, the copy step tolerates a missing `docs/setup.md` only in a local
  run and fails in a release
- a tag or a release run: the next real release proves the change

## Steps

1. [ ] `scripts/release.ps1`: the four files, the images folder when present
2. [ ] `scripts/changelog.py --notes`: the footer
3. [ ] `docs/development.md` and `SECURITY.md`
4. [ ] The zip file-list test, if one exists
5. [ ] Verification: a local `release.ps1 -Tag v0.1.0` run without a certificate, the zip's file list, `dotnet test`,
       the `lint` and `workflow-lint` checks locally

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit

## Deviations from the plan

- none yet

## Verification

- pending

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
