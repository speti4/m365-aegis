---
status: decided
date: 2026-10-28
cost: cheap
sources: [wip/001-project-scaffolding.md, D-002-dotnet-10-core-cli-and-window-with-react.md]
---

# D-005 — A private repo under a CI minutes budget; the tenant's data never in the repo

## Context

The tool administers the employer's tenant, is the maintainer's personal project, and will be shared with the
support team as a build. A private repository bills every Actions minute (GitHub Pro: 3,000 a month, shared with
other repos, Windows at twice the rate), and every file may carry something that identifies the tenant.

## Decision

- The GitHub repository `speti4/m365-aegis` is **private** from its first commit. Publishing is a separate
  decision at M6.
- **CI runs under a minutes budget:** the Linux jobs (`dotnet`, `deps`, `frontend`, `sidecar`, `lint`,
  `workflow-lint` and `commit-rules`) run on every push to `main` and every pull request; the one Windows job
  (`windows`: the published exes, the window's smoke test, the Windows-only tests) runs on pull requests and tags
  only, and the `release` job on a tag. The ruleset requires every job, a skipped one included. No macOS job.
- **The tenant's data never enters the repo:** no tenant name or id, client id, user principal name, mailbox
  address, object id, sign-in record, token or certificate. The maintainer's tenant and app registration stay in
  the gitignored `CLAUDE.local.md`; the tool's own files live under `%LOCALAPPDATA%\m365-aegis\`. Fixtures are
  scrubbed to `contoso.com` and `00000000-0000-0000-0000-0000000000NN` ids, and `scripts/check-fixtures.py` holds
  them to that in CI. Every file is written as public text anyway (a task file's `plan:` is `none`, no home
  folder path, decisions and results instead of conversations), so going public needs no rewrite.
- Cost: cheap. Decided 2026-10-28.

## Not chosen

- Public from the first commit with the full matrix on every push (the mrgl choice): the tool is about one
  employer's tenant and is not a community tool yet; the minutes concern is real on a private repo.
- The Windows job on every push: twice the rate for a check the pull request already ran.
- Tenant identifiers in the repo because it is private: a private repo is one setting away from public, and the
  files are read by agents and copied into issues.
