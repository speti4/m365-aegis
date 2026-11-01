---
size: M
origin: wip/102-access-view-as-a-window-card.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M4 Window
---

# 112 — A user picker shared by the User access and the Mailbox access cards

## Context

A picker component that suggests users by name or address as the colleague types (the user lookup's read over
the bridge), used by the User access card (task 102) and the Mailbox access card instead of their plain text fields;
the picked user's address goes to the bridge request.
Why: task 077 left no reusable picker, so both cards ask for an address typed in full; the support colleague knows
names, not user principal names.
Done when: over the mock transport typing three letters lists matching users, picking one fills the card's request, a
Vitest test holds it over a bridge sample, and the usage guide's card paragraphs describe the picker.
Twelfth round of 2026-11-01, on one branch with tasks 110 and 113; the task session owns the steps below and may reshape
them. The maintainer's click-through of the real window is optional and the main session's.

## Docs that move

- `docs/usage.md`: the User access and Mailbox access card paragraphs describe the picker
- `tests/contracts/`: a bridge sample for the picker's lookup request and answer, if the existing user lookup sample
  does not serve

## Not in this change

- A picker for groups or mailboxes
- Any change to the Core's user lookup read beyond what the bridge needs (a prefix search is the read of task 077)

## Steps

1. [x] A user search the bridge can answer (see Deviations): `user-search` `{text}` answered with `user-matches` (its
       source named) or `no-snapshot`: the directory live through Graph, the newest snapshot when Graph cannot answer;
       the request and reply samples, the Graph fixture; Core tests for both sources and the fallback
2. [x] The picker component over it: suggestions after three letters, keyboard and mouse pick, the picked user's address
       handed to the card; a 250 ms debounce so one keystroke sends at most one request, an older answer dropped
3. [x] The User access card and the Mailbox access card ("A user") use it in place of their text fields; the plain
       address still works when typed in full
4. [x] Vitest holds the suggestions, the pick and the request over the bridge samples, 100% coverage held; Biome and the
       typecheck clean; the GUI `--smoke` passes
5. [x] Docs under *Docs that move* and `docs/development.md` (the request, the picker); the Verification dated;
       `tasks_check` without `write` clean apart from the stale index

## Log

- 2026-11-01 — promoted on `task/112-user-picker-and-window-words` by the main session
- 2026-11-01 — no user search existed: task 077's "user lookup" is the `mailbox-access` user direction (which mailboxes
  a user holds rights on), not a search by name. The source chosen for the suggestions is the newest snapshot, not a
  live Graph `/users` filter: the User access card answers from the snapshot only, so its suggestions are exactly the
  users it can answer for; it needs no sign-in and no new scope, and a keystroke costs a SQLite query on this computer,
  not a Graph request. The Mailbox access card's "A user" uses the same picker; a user created since the snapshot is
  typed in full there.
- 2026-11-01 — Core: `SnapshotAccess.FindUsers(home, profile, text, limit = 8)`, the `user` objects whose name, a word
  of it (`name like '% ' || prefix`), user principal name or address starts with the text, in any case, `%`, `_` and
  `\` escaped, by name, `more` when a ninth matches; `UserMatches(Snapshot, Users, More)`. The bridge:
  `BridgeMessageType.UserSearch`/`UserMatches`, `UserSearchRequest(Text)`; the dispatcher's `access` and `user-search`
  share `FromSnapshot` (the `no-snapshot` state). Samples `user-search.json` (`exa`) and `user-matches.json` (the five
  `* Example` users of the fixture snapshot); `BridgeAccessTests` hold the reply to the sample without a sign-in or a
  Graph request, the `no-snapshot` state and the usage error; `SnapshotUserSearchTests` the word start, the address,
  no group, no middle of a word, the limit and `more`, the wildcards, no snapshot, no text.
- 2026-11-01 — front end: `components/user-picker.tsx`, an ARIA combobox (a `combobox` field, a `listbox` of
  `option`s, `aria-activedescendant`); the list sits in the flow under the field, since the card's `overflow-hidden`
  clips a floating one; a failed search shows nothing (the card's own lookup reports the error), `no-snapshot` and no
  match are a line under the field. A pick looks the user up at once on both cards. The mock answers from the five
  fixture users (`mockUserSearch`), held equal to the sample.
- 2026-11-01 — a look in the dev server over the mock (Playwright CLI): "exa" listed the five users under the User
  access field, the arrows highlighted Dave, Enter showed his access view.
- 2026-11-01 — the maintainer's decision on the first pull request: the picker suggests from a live Graph search, the
  snapshot only as the fallback. The rework: `UserSearch.FindAsync` (`Groups/UserSearch.cs`), one `/users` request
  through the window's Graph client and its silent sign-in, `$search="displayName:<t>" OR "userPrincipalName:<t>" OR
  "mail:<t>"` (the display name tokenized, so a word's start matches; the other two by their start), `$count=true`,
  `ConsistencyLevel: eventual`, `$orderby=displayName`, `$select` of the four fields, `$top=9` for the `more` mark,
  under `User.ReadBasic.All` (already in the permissions table for the group lookups and the take; no new permission);
  a quote in the text is dropped, as the group search drops it. The dispatcher asks it first; any `AegisException` but
  a usage one (no silent sign-in, 403, the network) falls back to `SnapshotAccess.FindUsers`, and `no-snapshot` without
  a snapshot. `UserMatches` gained `Source` (`graph` or `snapshot`, the `UserSource` enum) and `Taken`, its `Snapshot`
  only when the snapshot answered.
- 2026-11-01 — the Graph fixture `users@$count=true&$orderby=displayName&$search=%22displayName%3Aex~a9f815d6.json`: the
  five `* Example` users as the recorded `/users` answer has them (Carol without an address), so `user-matches.json` is
  now the directory's answer (`source: graph`). Core tests: `UserSearchTests` (the one advanced query and its header,
  `more` at a ninth user, a quote dropped, an empty answer, a text of quotes a usage error); `BridgeAccessTests` (the
  sample from the directory with one Graph request; signed out, a transport that throws the silent sign-in's `auth`
  failure, the snapshot answers with its name and time; a 403 from Graph falls back too; signed out without a snapshot
  is `no-snapshot`; a text of quotes is the usage error, not a fallback).
- 2026-11-01 — front end: `UserMatches` carries `source`, `snapshot` and `taken`; the line under the list says "From
  the directory." or "From the snapshot taken <UTC minute>: the directory cannot be searched now (signed out, or it did
  not answer).", beside the no-match and `more` lines; `no-snapshot` says "No suggestions: the directory cannot be
  searched now and this computer has no snapshot. Type the full address.". `describeTaken` moved to
  `components/snapshot-time.ts` (the card re-exports it), so the picker and the card share it without an import cycle.
  The mock answers from the directory signed in and from its snapshot signed out.
- 2026-11-01 — `origin/main` merged (#151, the take's bulk reads): the wording test still reads 13 parts from
  `SnapshotTake.cs`.

## Deviations from the plan

- A Core change the Context did not foresee ("Not in this change": nothing beyond what the bridge needs): the bridge
  had no search at all, so a new request (`user-search`) and its read (`SnapshotAccess.FindUsers`) were added; the
  command line has no such command. The first pull request searched the snapshot only; the maintainer decided for the
  live Graph search with the snapshot as the fallback, and this branch does that (the Log has both). The Graph read
  is a new Core read (`UserSearch`) under a permission the table already names.

## Verification

- 2026-11-01 — the rework on the tree merged with `origin/main` (#151), all exit 0: `dotnet restore --locked-mode`,
  `dotnet format --verify-no-changes`, `dotnet build -c Release` (0 warnings, the front end included), `dotnet test`
  (1606 tests, 0 failed), the coverage gate (`M365Aegis.Core.dll` lines 6525/6525, branches 3367/3367; `m365-aegis.dll`
  lines 2145/2145, branches 538/538), `changelog.py --check`, `check-fixtures.py --self-test` (17 cases) and the scan
  (518 files, 0 findings); `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (213 tests, 100%
  statements, branches, functions, lines), `npm run build`; `doctor --offline` exit 0, `whoami` exit 3 on an empty
  `M365AEGIS_HOME`, `m365-aegis-gui.exe --smoke` ready after 577 ms, `scripts/release.ps1 -DryRun` (the zip packed).
  The picker's new Vitest cases: "From the directory." under the matches and the no-match line; signed out over the
  mock, Dave suggested from the snapshot with "From the snapshot taken 2026-10-31 06:00 UTC: …"; signed out without a
  snapshot, the "No suggestions" line; a snapshot answer without a time named without one. The earlier entries below
  are of the first pull request's snapshot-only search.
- 2026-11-01 — in the worktree, all exit 0: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  `dotnet build -c Release` (0 warnings, the front end included), `dotnet test` (1590 tests, 0 failed), the coverage
  gate (`M365Aegis.Core.dll` lines 6263/6263, branches 3231/3231; `m365-aegis.dll` lines 2128/2128, branches 528/528),
  `changelog.py --check`, `check-fixtures.py --self-test` (17 cases) and the scan (515 files, 0 findings); `npm ci`,
  `npm run lint`, `npm run typecheck`, `npm run test:coverage` (211 tests, 100% statements, branches, functions,
  lines), `npm run build`; `doctor --offline` exit 0, `whoami` exit 3 on an empty `M365AEGIS_HOME`,
  `m365-aegis-gui.exe --smoke` ready after 769 ms, `scripts/release.ps1 -DryRun` (the zip packed).
- Vitest of the picker (`user-picker.test.tsx`, fake timers): no request under three letters; "car", "caro", "carol"
  typed within the debounce send one `user-search` for "carol"; ArrowDown and ArrowUp wrap, Enter picks
  `bob@contoso.com` and takes the key, Tab and Enter without a highlight are left to the form; Escape and leaving the
  field close the list; a click picks, its mousedown's default prevented; the no-match, `more` and no-snapshot lines;
  an answer to an older text dropped, a failed search shows no list; a disabled field or an unmounted picker sends
  nothing. The cards: the User access card sends `user-search {"text":"Dav"}` then `access {"user":"dave@contoso.com"}`
  on the click; the Mailbox access card suggests nothing for "A mailbox" and looks Bob up after the pick under "A
  user". The mock's answer to the `user-search` sample equals the `user-matches` sample.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
