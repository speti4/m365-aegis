---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 071 — A write-guard test over the whole command tree: without `--yes`, nothing is sent

## Context

One table-driven test in the Cli tests that runs every write command (`group members|owners add|remove`,
`mailbox access grant|revoke`, `mailbox forwarding set|clear`, `call` with a non-GET method, each CSV form and
`--resume`) without `--yes` and with `--dry-run`, and asserts that the fixture transport recorded no request
other than GET and the fake sidecar no cmdlet outside the read list, while the exit code is 7 or 0 and the plan
is printed.
Why: SECURITY.md names a write that happens without a confirmation as a vulnerability (D-006); today each
command's tests cover its own guard, and a new write command could land with none.
Done when: the test enumerates the write commands from the command tree itself, so a new write command joins it
without an edit, and it is green.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. The coverage bundle (070) moves the Cli tests to an in-process entry after
this bundle merges, so a new test here uses the harness of today (`CliTests.RunWith`) and stays small.

## Docs that move

- `docs/development.md`, "Writes and plans" (or "Tests"): one sentence naming the guard test as the contract's check
- `SECURITY.md` only if it names the tests that hold the scope (else none)

## Not in this change

- A new write command or a change to the guard itself
- The writes of the window (the bridge has its own confirmation path)

## Steps

1. [ ] The enumeration of the write commands from the built command tree (every command that carries the guard
       flags), the inputs each needs (a group, a mailbox, a CSV file, a record) as a table
2. [ ] The run without `--yes` and with `--dry-run` per command; the transport's request log and the fake module's
       write file asserted empty of writes; exit 7 or 0 and the plan in the output
3. [ ] The sentence in the docs

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
