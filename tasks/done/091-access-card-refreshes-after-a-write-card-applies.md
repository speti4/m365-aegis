---
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M4 Window
---

# 091 — The mailbox access card refreshes after a write card applies a change to the mailbox it shows

## Context

When a write card's apply finishes (task 082's `plan-applied`) for a mailbox the mailbox access card is showing, the
access card reads the mailbox again and marks the refresh; a failed or partial apply refreshes too, so the card shows
what the service holds now.
Why: after a grant the access card shows the old list until the colleague asks again, which looks like a grant that did
not happen; the browser mock's apply changes nothing, so the stale state is visible there too.
Done when: over the fake module an apply on the shown mailbox is followed by one `mailbox-access` request and the new
grant on the card, an apply on another mailbox sends none, and the mock transport's apply changes its state so the
browser shows the refresh.
Tenth round of 2026-10-31, on one branch with task 090; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The front end": the refresh after an apply and the mock transport's stateful apply
- `docs/usage.md`: the mailbox access card's entry, if it describes what the card shows after a grant

## Not in this change

- A refresh of any other card (the user lookup, the group cards) after an apply
- A change to the bridge protocol: `plan-applied` of task 082 is the signal, as it is

## Steps

1. [x] The signal: the window's `plan-applied` handling tells the mailbox access card which mailbox an apply touched
       (success, failure or partial alike)
2. [x] The card: when the mailbox is the one shown, one `mailbox-access` request and a visible refresh mark; another
       mailbox's apply sends none
3. [x] The mock transport's apply changes its state so the browser (`npm run dev`) shows the refresh; a Vitest test
       holds the one-request and the no-request cases
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/090-window-plan-wording` by the main session
- 2026-10-31 — the signal: `gui/web/src/components/applied-writes.ts`, the page's `AppliedWrites` channel (`App`
  makes one). A write card publishes after an apply that may have changed the mailbox: a `plan-applied` reply (every
  change, some or none made) and an apply that broke off on the way (an `error` other than `usage` or
  `refused-without-yes`, the codes that mean nothing was sent). The message carries the plan's operation, the mailbox
  as the card asked for it and the addresses in the changes' targets ("Finance (finance@contoso.com)"), so a mailbox
  asked by a short name still matches. The bridge protocol is unchanged.
- 2026-10-31 — the card: `MailboxAccessCard` listens while it shows a mailbox's holders; after a grant or a revoke
  whose name or addresses are the mailbox's (display name, address, user principal name or object id, any case) it
  sends one `mailbox-access` for the mailbox's address, shows "A change was applied to this mailbox: reading its
  access again…" while it waits and "Read again after a change on the Mailbox rights card." over the new table; a
  lookup of its own clears the mark, and a failed read is the card's toast as any lookup's.
- 2026-10-31 — the browser mock: `MockTransport` keeps its own copy of the holders and the forwarding, and
  `MockWrites` holds each plan with one step per change, which its apply runs for every change it applies (a grant
  adds the holder as the host reads it back, a Send on Behalf delegate by name where the Admin API reads the mailbox;
  a revoke removes it; a forwarding set or clear changes the forwarding). A `mailbox-holders` reply is a copy, so the
  card's table changes only with the refresh.
- 2026-10-31 — tests: `applied-writes.test.tsx` (the addresses, the match, the channel; the access card with the two
  write cards over the browser mock: one `mailbox-access` after a grant on the shown mailbox and the new row, after a
  partial apply and a revoke, none after an apply to another mailbox, a forwarding, with no mailbox shown or a user's
  grants shown; over a scripted host: told after a failed `plan-applied` and an `internal` error, not after a refused
  apply or a reply that is no apply's, the waiting line, a failed refresh as the card's error), `mock-writes.test.ts`
  (an apply changes the mock's state, a fresh mock's is the original), `App.test.tsx` (the page wires the cards: a
  grant on the rights card, the access card goes from 3 to 4 grants with the mark).
- 2026-10-31 — docs: `docs/development.md` ("The front end": the access card's refresh, "The refresh after an apply",
  the mock's stateful apply), `docs/usage.md` ("Mailbox access": the card reads the mailbox again after a grant or a
  revoke).
- 2026-10-31 — merged in #120 (squash); closed on `main` by the main session. The follow-up filed from the pull
  request: the user view marked stale with a "Read again" button as task 103

## Deviations from the plan

- Only a grant or a revoke refreshes the card: a forwarding apply on the shown mailbox sends no request, as the card
  shows no forwarding. The done-when's "an apply on the shown mailbox" is read as an apply that changes what it shows.
- Added beyond `plan-applied`: an apply that broke off on the way (an `error` reply other than `usage` or
  `refused-without-yes`, the card's "Stopped: check the mailbox before you try again") refreshes too, since some
  changes may have been made.
- A card that shows a user's grants (the reverse lookup) is not read again: that lookup reads every mailbox over
  several sidecars (41 s over 620 mailboxes on the command line), too heavy to repeat on its own; a follow-up.
- "Over the fake module": the core side, a grant applied over the fake module and visible to the next
  `mailbox-access`, is held by task 082's `BridgeWriteTests`; this task's request side is held over the browser mock
  and a scripted host in Vitest. The real window was not opened in this session.

## Verification

- 2026-10-31 — the local matrix in the worktree, all exit 0 (the same run as task 090's): restore, format, build, 1443
  .NET tests, the coverage gate at 100% in Core and Cli, the changelog and fixture checks, the front end's lint,
  typecheck, `test:coverage` (170 tests, 100% in every measure), build, and the `windows` job's steps (doctor, whoami
  exit 3, `--smoke`, the release Pester tests and the signed dry run).
- 2026-10-31 — the browser mock (`npm run dev` on a port of its own, `playwright-cli`): Finance looked up on the access
  card (3 grants), then a FullAccess grant for bob@contoso.com on Finance from the rights card, confirmed: the access
  card read Finance again on its own (4 grants, bob's FullAccess row from PowerShell) under "Read again after a change
  on the Mailbox rights card."

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
