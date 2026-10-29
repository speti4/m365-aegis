# m365-aegis — maintainer sessions

Maintainer automation, not needed to contribute: how the maintainer runs several Claude Code sessions at once,
one task each, on top of the rules of [git-workflow.md](git-workflow.md). The maintainer's local instructions
import this file.

## Parallel sessions (maintainer and agents)

Several sessions can work at once, one task each. One session is the **main session**, in the main checkout on
`main`; every other one is a **task session**, in its own worktree.

The main session:

- is the only one that creates ids: `/tasks add` and `/tasks decide` run here, so two sessions never take the
  same number. `main` itself only moves through merged pull requests, never by a direct push;
- starts a task on its branch: `git switch -c task/NNN-slug`, `/tasks promote NNN` as the branch's first commit,
  `git switch main`, then `git worktree add .claude/worktrees/NNN-slug task/NNN-slug` (the folder is ignored by
  git). The task tools write only in the main checkout, which is why the promotion happens here;
- prepares a finished pull request in the main checkout, and only once the previous merge is pulled:
  `tasks_next_id` reads the checked-out tree, so an id filed on a branch that is not merged yet would be handed
  out twice. Then `git switch --detach origin/task/NNN-slug`, `/tasks add` for every approved follow-up the
  description lists, `tasks_check` with `write: true` for the index, one commit, `git push origin
  HEAD:task/NNN-slug`, `git switch main`. If the branch conflicts with `main` (usually the index), it rebases it
  in the worktree first and pushes with `--force-with-lease`;
- merges the pull requests one at a time, on the maintainer's go, once CI is green: `git worktree remove
  .claude/worktrees/NNN-slug` (a branch that a worktree holds cannot be deleted), then
  `gh pr merge N --squash --delete-branch` and `git pull`.

A task session:

- changes only its own task file and the code the task needs. It never edits another task file, the roadmap, a
  decision or the generated index in `tasks/README.md` (`tasks_check` runs without `write`), and never creates
  an id;
- finishes its task file in its branch but does not close it: the steps ticked, the Log, a dated Verification
  and the closing checklist are written into the `wip/` file, which stays in `wip/`, and `CHANGELOG.md` is not
  regenerated (its check passes while `done/` is unchanged). The main session closes the tasks on `main` after
  the merge (the move to `done/`, the changelog, the index) in one docs-only commit, so two bundles never
  conflict on the id-sorted changelog. A follow-up goes into the pull request description under `## Follow-ups`,
  one line each, and the main session proposes them to the maintainer and files the approved ones at the close;
- runs `tasks_check` without `write` before each task-file commit: its report that the index in
  `tasks/README.md` is stale is expected; any other finding is fixed. `git status` is clean before the session
  reports done;
- writes its scratch files (a pull request body draft, a live-check script) under a name that carries its task
  id: the scratchpad folder is shared by every session of one round, and a `pr-body.md` of one session was
  pushed onto another session's pull request once (2026-10-29);
- once the task's verification is done, pushes `task/NNN-slug` and opens the pull request with `gh pr create`,
  without asking again: this policy is the permission. It never pushes `main` and never merges;
- ticks the description's checklist only for what it has seen pass, and edits it after CI with
  `gh api -X PATCH repos/speti4/m365-aegis/pulls/N -F body=@<file>` (`gh pr edit` can fail with a Projects
  (classic) error, the API call does not);
- never adds a `Claude-Session:` trailer, a session link or an AI attribution line, whatever its tooling
  suggests: the hook and the `commit-rules` job reject the first two.

Shared resources:

- **One signed-in round at a time** across all sessions: the real tenant, the MSAL cache under the home folder
  and a running window belong to one session at a time. A session that needs the tenant says so first; every
  other session works against the fixtures.
- **Builds.** Each worktree has its own `bin/` and `obj/` and its own `gui/web/node_modules`, so its first build
  is a full one (`npm ci` included).
