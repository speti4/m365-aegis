---
size: S
origin: wip/082-window-mailbox-write-cards-behind-a-plan-and-a-confirmation.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M4 Window
---

# 090 — The window's plan warnings in the window's own words, not the command line's flags

## Context

A plan warning shown on a card is worded for the window: the forwarding card's "(--keep-copy keeps one)" becomes a
sentence about the "Keep a copy" box, and every other warning the `Plan` carries is checked for a flag name; the
command line's wording stays as it is, the window maps or rewords it at the card.
Why: the support colleague never sees the command line (the M4 done-when), and a flag name on a card is a dead end.
Done when: no card of the window renders a `--` flag over the fake module and the mock transport, a Vitest test holds
it over the bridge samples, and the command line's plan text is unchanged (its snapshots untouched).
Tenth round of 2026-10-31, on one branch with task 091; the task session owns the steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The front end": where a plan warning is reworded for a card, if the mapping gets a place of
  its own
- `docs/usage.md`: a card's text if the guide quotes a warning

## Not in this change

- The command line's plan wording and its snapshots
- A change to the bridge protocol or its contract samples (the window rewords what it receives)

## Steps

1. [x] The inventory: every warning a `Plan` can carry (the Core's plan builders) and which ones name a flag
2. [x] The mapping at the card: the forwarding warning becomes a sentence about the "Keep a copy" box; every other
       flag-naming warning gets its window wording; an unknown warning passes through unchanged
3. [x] A Vitest test over the bridge contract samples: no card renders a `--` flag; the CLI snapshots are untouched
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/090-window-plan-wording` by the main session
- 2026-10-31 — the inventory, from the core's sources (`PlanBuilder.Warn` and the resume's `warnings.Add`): the
  mailbox writes' five (`Exchange/MailboxWrites.cs`: a recipient forwarding the plan clears, an address outside the
  accepted domains, a forwarding without a copy, a holder that cannot hold a right, a group holder), the group writes'
  four, `call`'s one, `plan prune`'s one and the resume's one. Two name a flag: the forwarding without a copy
  ("(--keep-copy keeps one)"), which the forwarding card plans, and `plan prune`'s "--resume cannot continue them",
  which only the command line plans. The group writes' last-owner warning names a command (`group owners-missing`),
  no flag; no card plans a group write yet.
- 2026-10-31 — the mapping at the card: `gui/web/src/components/plan-wording.ts` (`windowWarning`, the core's
  sentences as patterns and the window's sentence for each); `PlanView` shows every warning through it. The forwarding
  warning becomes "<mailbox> keeps no copy: the mail reaches <address> only. To keep one in the mailbox too, choose
  "Change the picks" and tick "Keep a copy in the mailbox"."; a warning the list does not know passes unchanged. The
  confirmation's own note named `--yes` ("the command line asks for the same confirmation with `--yes`"); it now says
  "Going back changes nothing: the window sends no change until you confirm.". The core and the command line are
  untouched, so their snapshots are too.
- 2026-10-31 — tests: `plan-wording.test.tsx` holds the rewording and the pass-through, reads the core's sources and
  fails when a warning that names a flag is added (the list is the two above, and every mailbox write's warning comes
  out of `windowWarning` without a flag), renders the plan, the confirmation, the result and an error over the bridge
  samples (`plan.json`, `plan-applied.json`, `error.json`) with every mailbox write's warning added, and drives the
  forwarding card over the browser mock from the picks to the result: no `--` flag on any of them.
- 2026-10-31 — docs: `docs/development.md` ("The front end", the write cards: where a warning is reworded and the two
  tests), `docs/usage.md` ("Mailbox forwarding": what the plan says without the copy).
- 2026-10-31 — merged in #120 (squash); closed on `main` by the main session. The follow-up filed from the pull
  request: the last-owner warning in the window's words as task 104

## Deviations from the plan

- Added beyond the warnings: the confirmation's own note named `--yes`, so "no card renders a `--` flag" changed it
  too. The card descriptions still name the command line's commands (`m365-aegis mailbox access grant|revoke`), no
  flag; the design sketch of task 018 (dev only, not in `dist/`) keeps its `--yes` note.
- The guard over the core is a Vitest test that reads the core's C# sources, not a C# test: the window's mapping lives
  in TypeScript, and the core does not change.

## Verification

- 2026-10-31 — the local matrix in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore`, `dotnet build --no-restore -c Release` (the front end built into `wwwroot`, 0
  warnings), `dotnet test` (1443 tests, 0 failed, `UsageGuideTests` included), the coverage gate (`M365Aegis.Core.dll`
  5202/5202 lines and 2871/2871 branches, `m365-aegis.dll` 1894/1894 and 468/468), `python -I scripts/changelog.py
  --check`, `python -I scripts/check-fixtures.py` (331 files, 0 findings); the front end: `npm ci`, `npm run lint`,
  `npm run typecheck`, `npm run test:coverage` (170 tests, before 151; 100% statements, branches, functions and lines),
  `npm run build`; the `windows` job's steps: `doctor --offline` exit 0, `whoami` exit 3 on an empty
  `M365AEGIS_HOME`, the window's `--smoke` exit 0 (ready after 832 ms), `Invoke-Pester tests/release` (7 passed),
  `scripts/release.ps1 -DryRun` with a throwaway certificate. No package was added.
- 2026-10-31 — the browser mock (`npm run dev` on a port of its own, driven with `playwright-cli`): a forwarding of
  Alice to alice@fabrikam.com without the copy shows the reworded warning beside the accepted-domains one, and the
  page's text holds no `--` flag. The command line's wording is unchanged: `MailboxWrites.cs` and the snapshots are
  not in the diff. The real window over the fake module or the tenant was not opened in this session.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-31` by the main session after the merge
