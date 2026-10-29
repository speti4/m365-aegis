---
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 026 — A dated note in D-003: the broker's silent path never reaches the file cache's refresh token

## Context

A "Note 2026-10-29" in `tasks/decisions/D-003-own-public-client-with-msal-and-wam.md`, next to the note of task
013: with the broker enabled, MSAL.NET answers a silent call for an account the broker lists from the broker
alone and never falls back to the refresh token the file cache holds from a device-code sign-in, so the adapter
tries the same account through a second client over the same cache without the broker; the file cache therefore
holds the full token set of a device-code sign-in, not only accounts and id tokens.
Why: D-003 states what the file cache holds and why it is registered; task 002 measured a case that the
decision's text does not cover, and a decision file is the main session's to edit.
Done when: the note is in D-003 with the date and the measurement, and `tasks_check` is green.

## Docs that move

- `tasks/decisions/D-003-own-public-client-with-msal-and-wam.md`: the note

## Not in this change

- any change to the adapter (task 002 shipped the fallback) or to `docs/development.md` (task 002 documented it)

## Steps

1. [x] the note in D-003, dated, with the measurement of task 002
2. [x] `tasks_check` green

## Log

- 2026-10-29 — done in the main session on `main` (a decision note, nothing that builds)

## Deviations from the plan

- none

## Verification

- 2026-10-29 — the note sits between the task 013 note and "Not chosen" in D-003; `tasks_check` with `write`
  green; `python -I scripts/changelog.py` regenerated the changelog with the task under housekeeping

## Closing checklist

- [x] every follow-up is a `backlog/` file or deleted with a reason in the Log (none found)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-29`
