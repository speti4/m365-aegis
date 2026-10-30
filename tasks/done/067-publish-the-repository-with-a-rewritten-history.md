---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
closed: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 067 — Publish the repository: a rewritten history in a new public repo

## Context

The maintainer decided on 2026-10-30 to publish `speti4/m365-aegis`, on the condition that neither the tree nor
the git history carries personal data or real tenant information (D-010). The audit of the same day found no
tenant data anywhere (history, tree, pull request texts) and one kind of personal context: the `.claude/memory/`
files committed in four commits of 2026-10-28/29 (first name, workplace context, Hungarian prose, Claude session
ids, an old checkout path), plus the maintainer's name as commit author and copyright holder. Decided 2026-10-30:
the history is rewritten with `git filter-repo` in a fresh clone (the memory folder removed, `speti4` as author
and in the three files) and pushed to a new repository, since GitHub keeps force-pushed-away objects reachable;
the old repository is renamed `m365-aegis-private-archive` and stays private; the visibility flip is the last
step, on the maintainer's explicit go. Task 019 (the two public-only security settings) is done with this one.
Done when the new repository is public, its full history scans clean, the settings match task 012 plus 019, and
the main checkout points at it.

## Docs that move

- `LICENSE`, `Directory.Build.props`: the copyright holder and author become `speti4`
- `scripts/check-commit.sh`: `is_maintainer` and its self-tests know only `speti4`
- `docs/development.md`, "CI and releases": the private-era paragraph rewritten (the settings as applied, the
  free minutes of a public repository, pull request numbers below 27 refer to the archive)
- `.github/workflows/ci.yml`: the header comment's "the repo is private" sentence reworded to D-010
- `README.md`: the Decisions bullet names the public repo
- `tasks/decisions/D-010`: filed `proposed`; D-005 marked superseded

## Not in this change

- Any widening of the CI matrix (the `windows` job on every push): a separate decision, proposed as a follow-up
- The issue mirror of the task files (M6)
- Deleting the archive repository
- Any code change

## Steps

1. [x] Task files: 067 and 019 promoted, D-010 filed, D-005 superseded; the index; committed to `main`
2. [x] Branch `task/067-publish-the-repository`: the three name files, `docs/development.md`, `ci.yml`, `README.md`;
       pull request, CI green, squash merge, `git pull`
3. [x] Fresh clone `E:\projects\m365-aegis-rewrite`; `git filter-repo --invert-paths --path .claude/memory/
       --mailmap … --replace-text …`; reflog expire and gc
4. [x] Verification of the rewrite: the pattern scan of `git log --all -p` is empty (the two expected hits aside),
       the authors are `speti4`, `dependabot[bot]` and GitHub, no `.claude/memory` path in any commit,
       `check-fixtures.py` green, the anonilm scan clean
5. [x] GitHub: rename the old repo to `m365-aegis-private-archive`; create `speti4/m365-aegis` private; push `main`
6. [x] Settings as task 012 applied them: merge settings, the two rulesets (exported from the archive), Dependabot
       alerts and security updates, Actions permissions; fork pull requests need approval (after the flip: a
       private repository refuses the policy)
7. [x] Repoint the main checkout (`remote set-url`, `reset --hard origin/main`, gc, `user.name speti4`); delete the
       rewrite clone
8. [x] On the maintainer's go: visibility public; then private vulnerability reporting, secret scanning and push
       protection (task 019); a signed-out check of the Security tab
9. [x] End-to-end verification from a fresh fetch of the new repository; Verification written in 067 and 019

## Log

- 2026-10-30 — the audit: `git log --all -p` dumped and scanned for the tenant id, the client id, the UPNs, the
  employer's domain, home-folder paths, JWTs, session links and non-zero GUIDs: no tenant data; the only
  non-zero GUIDs are Microsoft's message-trace app id, the WebView2 client key and the fake module's manifest id.
  The `.claude/memory/` files sat in commits `87edff5`, `c80e2da`, `909f327` and `fae36ba` and left at `9b6214b`.
  The 26 pull request bodies and every issue and review comment scanned clean. Decided: a new repository, the name
  replaced by `speti4` everywhere, the old repository kept private as an archive.
- 2026-10-30 — promoted in the main session with 019, without a worktree (a settings task); D-010 filed `proposed`
  (`2cd5694` on `main`)
- 2026-10-30 — the tree changes through pull request #27 (`91e654d`): the name files, the docs, the workflow
  comments; the commit-rules self-test (38 cases), the changelog and fixture checks, zizmor, restore, format,
  build and 808 tests green locally, every CI job green on the pull request; the clone's `user.name` is `speti4`
  from here on, since the hook knows only that name
- 2026-10-30 — the rewrite in a fresh `--no-local` clone: `git filter-repo --invert-paths --path .claude/memory/
  --mailmap <speti4 for the name> --replace-text <the name to speti4>`, 54 commits in, 51 out (the three
  memory-only `chore(memory)` commits pruned), every other subject kept, the HEAD tree byte-identical to the old
  `origin/main`; `reflog expire --expire=now --all` and `gc --prune=now`, 1.49 MiB packed
- 2026-10-30 — GitHub: the old repository renamed `speti4/m365-aegis-private-archive` (private, pull requests 1–27
  and the Actions runs kept there), the new `speti4/m365-aegis` created private, `main` pushed (`c002a60`); the
  repository patched as task 012 did (squash only, PR_TITLE/PR_BODY, delete branch on merge, projects and wiki
  off), the two rulesets re-created from the archive's export (26216656 on `main`: deletion, non_fast_forward,
  required_linear_history, pull_request with squash, the eight required checks non-strict, bypass for the
  repository admin; 26216659 on `v*` tags), `vulnerability-alerts` and `automated-security-fixes` 204,
  workflow permissions read-only with no review approval; the fork pull request approval policy answered 422
  ("not allowed for private repositories") and waits for the flip
- 2026-10-30 — the main checkout repointed (`remote set-url`, `reset --hard origin/main`, the stale tracking ref of
  the task branch deleted, reflog expired, gc): `git cat-file -t ebd9e62` and `87edff5` answer "Not a valid object
  name"; the rewrite clone deleted
- 2026-10-30 — the first `ci` run of the new repository green on `c002a60` (the `windows` job skipped on a push, as
  on every push to `main`); on the maintainer's go `gh repo edit --visibility public
  --accept-visibility-change-consequences`, then task 019's two settings and the fork pull request approval
  policy (`all_external_contributors`, 204 once public)
- 2026-10-30 — close: two follow-ups approved and filed, 068 (the `windows` job on every push, now that the
  minutes are free) and 069 (the issue mirror of the task files); the deletion of the archive repository is not
  filed: the maintainer keeps it as the record of pull requests 1–27 and decides on it on their own, no task
  needed. D-010 decided.

## Deviations from the plan

- The archive rule in `docs/development.md` first said "below 27"; the squash commit of the tree changes is
  itself `(#27)`, so the sentence says "up to 27" since the docs-only commit that followed the merge

## Verification

- 2026-10-30 — a fresh bare clone of `speti4/m365-aegis` from GitHub, 51 commits, one ref (`refs/heads/main` at
  `c002a60`): `git log --all -p` scanned for `originSessionId`, the maintainer's names, the tenant id, the client
  id, the employer's domain, home-folder paths and session links: the only hits are the made-up
  `C:\Users\someone\…` test paths of the doctor's tests and the hook's own negative tests in
  `scripts/check-commit.sh`; `graphctl` appears in task 001's text and in the first `.claude/settings.json`'s
  `E:/projects/graphctl/.claude/memory` value (a drive path with no name, left as it was); the authors and
  committers are `speti4`, `dependabot[bot]` and `GitHub`; no commit ever touches `.claude/memory`; the HEAD tree
  equals the main checkout's; `check-fixtures.py` 284 files, 0 findings. The anonymizer over every commit message
  of the new history found two addresses, `nobody@example.com` and Dependabot's `support@github.com`, nothing
  else; its sidecar map was deleted. The old repository's pull request bodies and comments, scanned before the
  move, carried no tenant or personal data either.
- 2026-10-30 — after the flip: `gh repo view` shows `PUBLIC`, `isPrivate: false`; `gh api
  repos/speti4/m365-aegis/rules/branches/main` lists deletion, non_fast_forward, required_linear_history,
  pull_request (squash) and required_status_checks (`dotnet`, `deps`, `frontend`, `sidecar`, `lint`,
  `workflow-lint`, `windows`, `commit-rules`, strict=false); `security_and_analysis` shows secret scanning,
  push protection and Dependabot security updates enabled; `private-vulnerability-reporting` answers
  `enabled: true`; the fork pull request approval policy is `all_external_contributors`; a signed-out GET of the
  repository page and of `security/policy` answers 200 (the report form itself redirects to a sign-in, as
  GitHub's form does for everyone). `speti4/m365-aegis-private-archive` is PRIVATE. The main checkout builds
  and tests as before the move (808 tests, the same tree).

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-30`
