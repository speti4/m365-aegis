# m365-aegis — git workflow

How changes reach `main`: the rules for every contributor, checked by a hook and by CI.

## For everyone

- **Branch, pull request, squash merge.** A change reaches `main` through a pull request: CI has to be green
  (the ruleset on `main` requires `dotnet`, `deps`, `frontend`, `sidecar`, `lint`, `workflow-lint`, `windows`
  and `commit-rules`), and the pull request is squash-merged, so `main` stays linear with one commit per
  change. The squash commit's message is the pull request title and description, so write both as a commit
  message. The one exception is the maintainer's: the repository admin may push a small housekeeping commit
  (a task file, a memory note, a docs line, nothing that builds) to `main` directly under the ruleset's admin
  bypass; CI still runs on the push. Code always goes through a pull request.
- **Titles.** Commit subjects and pull request titles are imperative and start with a type in lower case, as
  `type: subject` or `type(scope): subject`, where the type is one of `feat`, `fix`, `docs`, `ci`, `chore`,
  `tasks` and `deps` (dependabot). A `Merge …` or `Revert "…"` subject passes as git writes it.
- **Trailers.** No `Claude-Session:` trailer and no session link in a commit message or a pull request
  description: they point to a private session and are rejected (the hook and the `commit-rules` job). No
  AI attribution line either: the commit is the maintainer's.
- **Author address.** The maintainer commits with the GitHub noreply address; contributors choose their own.
- **The hook.** Once per clone, `git config core.hooksPath .githooks` enables the `commit-msg` hook, which checks
  the rules above (`scripts/check-commit.sh`). The `commit-rules` CI job runs the same check over every pushed
  commit and over the pull request text, whose title is held to the subject rule, and runs again when the title
  or the description is edited, so a skipped hook still fails the pull request.
- **Dependabot.** Its pull requests carry the `deps:` prefix and are squash-merged once CI is green.

How the maintainer runs several sessions at once is in [maintainer-sessions.md](maintainer-sessions.md).
