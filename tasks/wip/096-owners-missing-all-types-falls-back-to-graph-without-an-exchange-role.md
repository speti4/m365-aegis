---
size: S
origin: wip/089-group-owners-missing-judges-distribution-groups-by-managedby.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M3 Writes with safety
---

# 096 — `group owners-missing --all-types` falls back to Graph's list when the account has no Exchange role

## Context

When the sidecar's `Get-DistributionGroup` is refused for lack of an Exchange role, `--all-types` answers with
Graph's list as it did before task 089, the backend stays `graph`, and a line under the table (a warning in the
envelope) says the distribution groups are judged by Graph's empty owner list because Exchange could not be asked.
Why: since task 089 the command fails outright without an Exchange role, where it used to give the Graph view; a
partial answer with a stated limit serves a reader better than an error, as `group owners` already does.
Done when: over the fake module with a refused Exchange sign-in the command exits 0 with Graph's list and the warning,
with the role it answers as task 089 made it, and the usage guide names the fallback.
Eleventh round of 2026-10-31, on one branch with task 098; the task session owns the steps below and may reshape
them. No live check: the maintainer's account has the role, so the refusal is the fake module's.

## Docs that move

- `docs/usage.md`: the `group owners-missing --all-types` paragraph names the fallback and its warning

## Not in this change

- The `owners-missing` judgment itself (task 089) when Exchange answers
- A fallback for any other refusal of the sidecar (a missing module, a failed connect): those stay errors

## Steps

1. [x] The refused-role case recognized in the `--all-types` read (the sidecar's error shape for a missing Exchange
       role, as `group owners` already tells it apart) and answered with Graph's list, backend `graph`, and the warning
2. [x] The fake module scripts the refusal; the Core and Cli tests hold exit 0 with the list and the warning, and the
       unchanged answer with the role
3. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/098-snapshot-diff-and-owners-fallback` by the main session
- 2026-10-31 — Core: `GroupLookups.OwnerlessAsync(ExchangeBackends, …)` catches a `forbidden` failure of the
  `ManagedBy` read (the kind `SidecarFailures` gives a 403, an access denial or a missing role, exit code 8 on its own,
  as `group owners` reports it) and answers with Graph's list as before task 089: `managedInExchange` left out, the new
  `OwnerlessGroups.Warnings` (null otherwise, so every other envelope is unchanged) with one warning that names the
  refusal and the role hint, and the backend `graph`: the new `ExchangeBackends.Forget` drops `pwsh` from the layers
  when the answer does without the layer that refused. Any other failure (sidecar unavailable, auth, throttling, a
  failed cmdlet) stays the error. Cli: the table prints each warning under the counts (`warning: …`).
- 2026-10-31 — fake module: `Assert-FakeRole` refuses the cmdlets that `M365AEGIS_FAKE_EXO_NO_ROLE` names
  (comma-separated) with "Access denied … (403)", called by `Get-DistributionGroup`. Tests: Core
  `DistributionGroupWritesTests` (the fake module refusing: five groups, Sales Managers among them, the warning,
  backend `graph`), `GroupLookupsTests` (a canned refusal falls back, a canned `sidecar-unavailable` stays the error),
  Cli `LookupCommandTests` (exit 0, the envelope's warning and backend, the table's warning line over the built exe).
  The usage guide's groups section names the fallback in a paragraph of its own.

## Deviations from the plan

- A connect that Exchange Online refuses with a 403 is the same `forbidden` failure and falls back too: the account
  cannot ask Exchange, as with a refused cmdlet. A connect that fails otherwise stays exit code 11.

## Verification

- 2026-10-31 — over the fake module with `M365AEGIS_FAKE_EXO_NO_ROLE=Get-DistributionGroup`, `group owners-missing
  --all-types` exits 0 with Graph's five groups, backend `graph` and the line "warning: the distribution and
  mail-enabled security groups are judged by Graph's empty owner list: Exchange Online refused the ManagedBy read (…)";
  without the variable it answers as task 089 made it (four groups, Sales Managers left out, backend `graph+pwsh`).
  `tests/sidecar/run-with-coverage.ps1`: 122 tests passed, the sidecar script's 266/266 commands. The rest of the local
  matrix is in task 098's Verification (one branch). No live check: the maintainer's account has the role.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
