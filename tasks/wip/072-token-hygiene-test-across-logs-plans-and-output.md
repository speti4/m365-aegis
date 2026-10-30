---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 072 — A token-hygiene test: a marked fake token never reaches a log, a plan record, an envelope or stderr

## Context

The test auth service hands out tokens that carry a unique marker; the test drives `login`, `whoami`, a
`--verbose` run with the MSAL log on, a plan apply with `--yes`, a failed apply and an error envelope, then scans
every file under the temp home folder (`logs\`, `plans\`, `config.json`) and the captured stdout and stderr for
the marker; the sidecar's `Hide-Secret` is covered on the Pester side with the same marker in a `Connect` reply.
Why: SECURITY.md's first scope item is a token written to a log, a plan record, an envelope or stderr; the
scrubbing exists in several places (the MSAL log filter, the plan record, the sidecar) and no test holds them
together.
Done when: the test is green, and injecting the marker into any of those sinks on purpose makes it red.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. The fake token source is the test's own (the fixtures' auth samples and the
fake module, never MSAL against a tenant).

## Docs that move

- `docs/development.md`: one sentence under the tests naming the hygiene test and the marker convention
- `tests/fixtures/README.md` only if a new fixture carries the marker

## Not in this change

- A change to where the product scrubs (a gap the test finds is fixed in the bundle and named under Deviations)
- The window's own log (`window:` lines) beyond what the shared Core sinks cover

## Steps

1. [ ] The marked token in the test auth path (the fixture transport's or the test token broker's token), the runs
       listed in the Context over a temp home, the scan of every file and of stdout and stderr
2. [ ] The Pester case: `Hide-Secret` over a `Connect` reply that carries the marker
3. [ ] The proof that the test is red when the marker is injected on purpose (a Log line with the injection and the
       failure, the injection not committed)
4. [ ] The docs sentence

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
