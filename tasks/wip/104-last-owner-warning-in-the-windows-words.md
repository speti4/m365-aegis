---
size: S
origin: wip/090-window-plan-warnings-in-the-windows-own-words.md
created: 2026-10-31
started: 2026-10-31
plan: none
milestone: M4 Window
---

# 104 — The group writes' last-owner warning in the window's words when a group write card arrives

## Context

The warning the group owner writes carry when a change leaves a group without an owner names a command (`group
owners-missing`); the window's wording map of task 090 rewords it at the card to what the colleague can do in the window,
and the Vitest test that forbids a `--` flag on a card also forbids a command name.
Why: a command name on a card is the same dead end as a flag for the colleague who never sees the command line (the M4
done-when); task 090 reworded the flags and left the command names for the card that will show them.
Done when: with a group write card over the mock transport no card renders a `--` flag or a `m365-aegis` command name,
the test holds it over the bridge samples, and the command line's text is unchanged.
Eleventh round of 2026-10-31, on one branch with tasks 102 and 103; the task session owns the steps below and may
reshape them. The window has no group write card yet: the wording and its test are written over the plan warning's
bridge sample, so the card that comes later meets them.

## Docs that move

- none (the wording map is the window's own; `docs/usage.md` names no command on a card)

## Not in this change

- A group write card itself (membership, owners) in the window
- The command line's warning text

## Steps

1. [x] The last-owner warning's wording in the window's map (`plan-wording.ts`), with the group write plan's warning in
       a bridge contract sample under `tests/contracts/`
2. [x] The Vitest test over every bridge sample forbids a `--` flag and a `m365-aegis` command name on a card; 100%
       coverage held; Biome and the typecheck clean
3. [x] The Verification dated; `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-10-31 — promoted on `task/102-access-card-and-window-words` by the main session
- 2026-10-31 — `plan-wording.ts`: the last owner ("…, and `group owners-missing` lists it") becomes "…only an
  administrator can manage it afterwards. To keep it managed, add another owner first; otherwise the administrator's
  list of groups without an owner names it."; the access view's three warnings that name `snapshot take` or
  `snapshot show` (task 102's card shows them) send to the administrator instead. A warning the map does not know
  still passes as the core wrote it
- 2026-10-31 — the sample `tests/contracts/bridge/plan.group-owners-remove.json`: the `plan` reply of a group owner
  removal that leaves Finance without an owner. The group write is no bridge request yet, so the Core test
  `TheGroupOwnerPlanSampleIsTheCoresPlanOfALastOwnersRemoval` plans it with `GroupWrites.PlanAsync` over the recorded
  answers, prints it as `--dry-run` does and holds the `plan` reply to the sample field by field
- 2026-10-31 — `plan-wording.test.tsx`: a `command` pattern (`m365-aegis` and a command word, or anything in backticks)
  beside the `--` flag one; every string of every reply sample (the warnings through the map) holds neither; the plan
  and confirmation of the group sample, the access views of the samples (with every access warning of the core) and
  the user access card over the mock hold neither; the existing plan, result, error and forwarding-card checks forbid
  the command too; a new warning of the core that names a command fails the inventory (today: the group writes' last
  owner, `plan prune`'s own, the access view's old snapshot)
- 2026-10-31 — the five cards' descriptions no longer name their command ("as `m365-aegis mailbox access` reads them"),
  so the cards over the browser mock carry no command name either

## Deviations from the plan

- The cards' descriptions are reworded too (the sign-in, group members, mailbox access, mailbox rights and mailbox
  forwarding cards): the test reads the whole card over the browser mock, and the done-when says no card renders a
  command name; the usage guide keeps naming the commands beside each card
- The group write plan's sample is a variant of `plan.json` named `plan.group-owners-remove.json`, held by a Core
  test to the core's own plan, since no bridge request makes it yet
- `docs/development.md` moved after all ("A command on a card"), though the task named no doc

## Verification

- 2026-10-31 — Vitest `plan-wording.test.tsx` 10 green (the rewordings, both inventories, every reply sample, the views
  and the cards over the mock); the suite 194 tests, coverage 100%; Core `BridgeAccessTests` holds the group sample to
  the core's plan
- 2026-10-31 — the command line's text unchanged: no file under `src/M365Aegis.Core` other than `Bridge/` changed;
  `GroupWritesTests.RemovingTheLastOwnerWarns`, `WriteCommandTests.RemovingTheLastOwnerWarnsInTheTable` and the
  envelope snapshot tests green in the 1544-test run

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
