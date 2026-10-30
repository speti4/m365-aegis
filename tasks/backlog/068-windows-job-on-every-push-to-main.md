---
status: idea
size: S
origin: wip/067-publish-the-repository-with-a-rewritten-history.md
created: 2026-10-30
milestone: M6 Release and team
---

# The `windows` job on every push to `main`, not only on pull requests and tags

What: the `if:` of the `windows` job in `.github/workflows/ci.yml` runs it on a push to `main` too (a docs-only
change still skips it through the `changes` job), and the D-005 sentence in `docs/development.md` follows.
Why: D-005 kept the Windows job off the `main` pushes to spare a private repository's billed minutes; since D-010
the repository is public and its standard runners are free, so the published exes and the window's smoke test
can run on the squash commit itself, not only on the pull request before it.
Done when: a push to `main` shows the `windows` job running and green, and the docs name the new rule.
