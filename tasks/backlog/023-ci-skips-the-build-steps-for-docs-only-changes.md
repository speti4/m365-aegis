---
status: ready
size: S
origin: session 2026-10-29
created: 2026-10-29
milestone: M1 Foundation
---

# CI skips the build jobs' steps when only docs, tasks, memory or the changelog changed

What: a first `changes` job in `ci.yml` diffs the pull request (or the push) against its base and sets a
`code` output; when every changed path is under `docs/`, `tasks/`, `.claude/memory/`, `CHANGELOG.md` or a
root markdown file, the `dotnet`, `windows`, `frontend`, `sidecar` and `deps` jobs skip their steps (an `if:`
on the steps, not on the jobs) and finish green in seconds. `lint`, `workflow-lint` and `commit-rules` keep
running. Not `paths-ignore`: a required check that never starts blocks the pull request under the ruleset.
Why: the three task-only pull requests of 2026-10-29 each waited four minutes for a Windows build of
unchanged code; the maintainer asked for a docs change to not cost a full CI round.
Done when: a pull request that touches only a task file reports every required check green within a minute,
a pull request that touches `src/` still runs the full build, and `docs/development.md` ("CI and releases")
names the rule.
