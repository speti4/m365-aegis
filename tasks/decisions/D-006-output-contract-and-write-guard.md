---
status: decided
date: 2026-10-28
cost: medium
sources: [wip/001-project-scaffolding.md, D-002-dotnet-10-core-cli-and-window-with-react.md]
---

# D-006 — One output contract (table or JSON envelope, stable exit codes) and every write behind a plan

## Context

The command line is read by a person at a terminal, by scripts in a pipe and by Claude Code sessions; the window
is used by people who do not read JSON. A write to the tenant is a change to other people's accounts. The earlier
draft and the research both asked for deterministic exit codes and a dry run.

## Decision

- **Two renderings of the same answer.** At a terminal a table; in a pipe, or with `--json`, the envelope
  `{"ok":…,"data":…,"error":{"code","message","hint"},"meta":{"requestIds","elapsedMs","backend"}}` on one line;
  `--table` forces the table. `ok` says which of `data` and `error` is set; a failure may still carry partial
  `data` (a doctor report, a bulk run). The code in `error` is stable and kebab-case.
- **Exit codes are a contract** (`M365Aegis.Core.Output.ExitCode`): 0 ok, 2 usage, 3 auth, 4 not found, 5
  throttled, 6 partial, 7 refused without `--yes`, 10 a required doctor check failed. A code never changes its
  meaning; a new condition gets a new number.
- **Every write is a plan.** A command that changes the tenant computes a plan (current state against desired
  state), prints it under `--dry-run`, and applies it only with `--yes`; without `--yes` it prints the plan and
  exits 7. The window shows the same plan and asks for a confirmation. Plans are recorded under the home folder.
- **Paging, throttling (429 and 503 with `Retry-After`), `$batch` and the v1.0/beta switch live in the core's
  client**, never in a command. Cost: medium, the shape of every command. Decided 2026-10-28.

## Note 2026-10-30 — the write guard as built (task 051)

The plan is the `Plan` type of the core (`Core/Operations`): the changes as current → desired per object, what is
already so, the warnings, and after an apply each change's result. `--dry-run` prints it with exit code 0, no flag
prints it with 7, `--dry-run` with `--yes` is a usage error (2), and a plan with nothing to change is exit code 0
under any flag, since nothing would be sent. An apply sends the changes one after another, goes past a change the
service refused and stops at one no later change could pass (the sign-in, the sidecar, the allow-list), marking the
rest skipped; every change applied is 0, some failed is 6 (`partial`), and no change applied with every one failing
the same way is that failure's own code (one change refused with 403 is 8), so no exit code was added. The record is
one JSON file per apply under the home folder's `plans\`, written when the apply starts and again when it ends.

## Not chosen

- JSON only: the administrator reads the table more often than a script reads the envelope.
- Table only, or a free-form text: an agent and a script would parse prose.
- A `--force` that skips the plan: the plan is the safety net for the hand-off to the team.
- Exit code 1 for every failure: a shell or an agent could not tell "sign in again" from "no such group".
