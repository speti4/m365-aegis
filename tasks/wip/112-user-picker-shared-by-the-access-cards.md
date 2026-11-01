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

1. [x] A user search the bridge can answer (see Deviations): `user-search` `{text}` answered with `user-matches` or
       `no-snapshot`, `SnapshotAccess.FindUsers` over the newest snapshot; the request and reply samples; Core tests
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

## Deviations from the plan

- A Core change the Context did not foresee ("Not in this change": nothing beyond what the bridge needs): the bridge
  had no search at all, so a new request (`user-search`) and its read (`SnapshotAccess.FindUsers`) were added; the
  command line has no such command. The source is the snapshot, not Graph (the Log has why); a live Graph search is a
  follow-up if the maintainer wants suggestions without a snapshot.

## Verification

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
