---
status: decided
date: 2026-10-30
cost: one-way
supersedes: D-005-private-repo-ci-budget-and-no-tenant-data.md
sources: [wip/067-publish-the-repository-with-a-rewritten-history.md, wip/019-private-vulnerability-reporting-and-secret-scanning-when-public.md, D-005-private-repo-ci-budget-and-no-tenant-data.md]
---

# D-010 — A public repository with a rewritten history, under the same no-tenant-data rule

## Context

D-005 made `speti4/m365-aegis` private from its first commit and deferred publishing to M6. The maintainer decided
on 2026-10-30 to publish, with one condition: no personal data and no real tenant information anywhere, the git
history included. The audit of 2026-10-30 (the full `git log --all -p` of 51 commits, the 26 pull request texts,
every comment, the repository metadata) found no tenant data at all: no tenant or client id, no user principal
name, no real domain, no home-folder path, no token. It found personal context in four commits of 2026-10-28/29
that carried the `.claude/memory/` folder before it was ignored: the maintainer's first name, workplace context,
Hungarian prose, Claude session ids and an old checkout path. The maintainer's name is also the author of most
commits and the copyright holder in three files.

GitHub keeps an object that a force-push abandoned reachable by its hash and through the pull request refs until
its support purges it, so a rewrite pushed into the existing repository would not meet the condition.

## Decision

- The history is rewritten once, in a fresh clone, with `git filter-repo`: the `.claude/memory/` folder removed
  from every commit, the author and committer `speti4` everywhere (mailmap), the name replaced by `speti4` in
  `LICENSE`, `Directory.Build.props` and `scripts/check-commit.sh` in every commit. The copyright line reads
  `Copyright (c) 2026 speti4`.
- The rewritten history is pushed to a **new** repository `speti4/m365-aegis`; the old one is renamed
  `speti4/m365-aegis-private-archive` and stays private (its pull requests 1–26, Dependabot pull requests and
  Actions runs are the maintainer's record). Pull request numbers below 27 in commit subjects refer to the
  archive.
- The new repository carries the settings of task 012 (squash only, the two rulesets, the eight required checks,
  Dependabot alerts and security updates) and, once public, private vulnerability reporting, secret scanning and
  push protection (task 019). Visibility changes to public as the last step, on the maintainer's explicit go.
- **The no-tenant-data rule of D-005 stays in force unchanged:** no tenant name or id, client id, user principal
  name, mailbox address, object id, sign-in record, token or certificate; fixtures scrubbed to `contoso.com` and
  `00000000-0000-0000-0000-0000000000NN` ids, held by `scripts/check-fixtures.py` in CI; the maintainer's tenant
  and app registration in the gitignored `CLAUDE.local.md`; every file written as public text. A memory note, a
  session id or a home-folder path is held to the same rule from now on.
- The CI matrix of D-005 (the Linux jobs on every push, the `windows` job on pull requests and tags) stays as it
  is; the minutes budget no longer binds, since a public repository's standard runners are free, and widening the
  matrix is a separate decision.
- Cost: one-way (a published history cannot be recalled). Decided 2026-10-30, the repository public since that
  day (task 067).

## Not chosen

- A force-push of the rewritten history into the existing repository: the pull requests would survive, but the
  abandoned objects stay reachable on GitHub until a support request purges them.
- Making the repository public as it is: the tree is clean, but the four memory commits are in the history.
- Keeping the maintainer's name as the commit author and copyright holder: a legitimate choice, declined by the
  maintainer in favor of the GitHub handle everywhere.
- Deleting the old repository: kept as a private archive for now; deletion is a possible follow-up.
