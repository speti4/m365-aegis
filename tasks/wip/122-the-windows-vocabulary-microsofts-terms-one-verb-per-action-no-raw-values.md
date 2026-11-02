---
size: M
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
plan: none
milestone: M4 Window
---

# 122 — The window's vocabulary: Microsoft's terms, one verb per action from the card to the plan, no raw values as text

## Context

One glossary for the window. "Full Access" as Microsoft writes it (the CLI still accepts `FullAccess`);
"permission" where the cards say right, grant or access ("Mailbox permissions" / "Change mailbox permissions"); the
plan badge and the confirm button use the card's verbs (Grant, Revoke, Forward, Stop forwarding), not the enum (add,
remove, set, clear), and the Core's titles read "Forward the mail of…"; raw values get display names through shared
maps (Shared mailbox; Microsoft 365, Security, Mail-enabled security, Distribution list; User, Group, Device; Owner;
Free/busy only; a snapshot named by its time, not its `.sqlite` file); Choose › Review › Confirm › Result with "Back
to edit"; plan warnings titled "Check before you confirm"; toasts and refusals through `sentence()`, hints say
"enter" not "give"; a `plural()` helper for every "(s)"; sentence-case badges; one-line card descriptions; people
named one way (sign-in name, email address, object ID; a site column is URL).
Why: the UX/UI review of 2026-11-02 (findings C-1, C-3 to C-11, D-11, U-11, D-10): a support colleague chooses
"Stop forwarding" and confirms a "clear" badge, reads "SharedMailbox" next to "Microsoft 365", and gets lower-case
command-line fragments as errors.
Decided 2026-11-02: last in the round, one sweep over the state tasks 120, 121, 123, 124, 125, 126 and 127 left
(the confirm button's names live in `confirmAction`, `plan-wording.ts`; the action badge variants by action in
`plan-view.tsx`; `card-parts.tsx` holds the shared parts). The README picture is retaken here, since this task
rewrites the group card's strings and task 126 moved its id column behind Details.
Done when: components, the Core texts the window surfaces, the tests and usage.md "The window" agree with the
glossary, and the review's seven usage.md mismatches are gone.

## Docs that move

- `docs/usage.md`, "The window" (every quoted label, button, badge, column) and the CLI sections where a window
  term is explained ("Full Access"); the seven mismatches the review listed
- `docs/development.md`, "The front end": the glossary's home (one line) and the `plural` / `sentence` helpers
- `docs/images/window-group-members.png`: retaken from the mock after the strings change (made-up data)
- `tests/`: the front-end tests' quoted strings; the Core snapshot tests for a changed plan title or warning

## Not in this change

- The CLI's own vocabulary and output (the command line keeps `FullAccess` as an argument and its table columns);
  only the Core texts the window surfaces (plan titles, warnings, the bridge's error hints) change, and only where
  the CLI does not print them as a contract (`M365Aegis.Core.Output` snapshots: check before changing a shared text)
- Any layout, color or behavior change (tasks 120–127 did those)
- Hungarian or any localization: the strings stay English

## Steps

1. [x] The glossary as code: a `vocabulary.ts` (or extend `plan-wording.ts`) with the display-name maps (mailbox
       types, group kinds, member types, sharing scopes, calendar roles, the action verbs), `plural(n, noun)` and
       the existing `sentence()` moved there; every card and `plan-view.tsx` reads them
2. [x] The write flow's words: Choose › Review › Confirm › Result, "Review the plan", "Continue", "Back to edit";
       the plan badge Grant / Revoke / Forward / Stop forwarding; "Check before you confirm"; the states
       "No Full Access" → "Has Full Access"; the Core's forwarding titles and the warnings in the window's words
       (`MailboxWrites.cs`; the CLI snapshots that quote them updated)
3. [x] The read cards' words: titles "Mailbox permissions" / "Change mailbox permissions", "Permission | User or
       group | Email address" headers, the empty states, the counts, the card descriptions as one plain line each,
       the placeholders and labels (sign-in name / email address / object ID; a site column URL), sentence-case
       badges, the diff's "How" values, the snapshot named by time
4. [x] Errors: the toast and `LookupError` texts through `sentence()`; the bridge's hints say "enter"/"choose";
       the ambiguous-group hint names the groups, not their ids (`GroupLookups.cs`, `BridgeDispatcher.cs`;
       the CLI's own hints untouched where the text is shared — a window-side rewording map is acceptable)
5. [x] Tests, docs under *Docs that move*, the README picture; the front-end checks; the .NET checks with the
       coverage gate if C# changed; a read-through of the mock in every state against the glossary

## Log

- 2026-11-02 — promoted on `task/122-window-vocabulary` by the main session
- 2026-11-02 — `gui/web/src/components/vocabulary.ts`: the glossary as code. `plural` (`Intl.PluralRules`, replaces
  `count` of `plan-wording.ts`), `sentence`/`capitalize` (an opening address or quote kept), the maps
  (`rightNames` moved here with "Full Access", `layerNames`, `mailboxTypeName`, `groupKindName` for both the bridge's
  and the snapshot's word, `objectTypeName`, `roleName`, `sharingAccess`, `calendarRole`, `diffHow` by part,
  `changeVerb` by operation, `snapshotTime` from a `.sqlite` name) and `windowError`: a rewording map for the core
  errors the command line shares (no group, an ambiguous group by name instead of object id, a holder not found, a
  forwarding address that is none) and the window's terms for any other ("enter", sign-in name, object ID, Full Access,
  a snapshot by its time), message and hint as sentences. `ErrorText`, the toast (`App.tsx`), a failed change's line
  and `windowWarning`'s fall-through all go through it.
- 2026-11-02 — the cards: Choose › Review › Confirm › Result, "Review the plan", "Continue", "Back to edit",
  "Review the plan again", "Preparing the plan. Nothing has changed yet…", "Making the changes, one at a time…"; the
  plan badge by `changeVerb`, the from → to states capitalized, "Check before you confirm" on plan warnings (the
  access cards keep "Good to know"); "Forward mail" on the forwarding confirmation (the card's verb, as Grant/Revoke
  permissions); titles "Mailbox permissions" / "Change mailbox permissions"; headers, empty states, counts,
  placeholders, labels ("Compare from"/"Compare to"), one-line descriptions, sentence-case badges ("Signed in",
  "Synced from on-premises", "Incomplete", "Not in the directory", "Added"/"Removed"), the token rows "Connected until
  3:00 PM" / "Not connected: …", the everyone line, the layers line under Details.
- 2026-11-02 — the Core: `MailboxWrites.cs` names "Full Access" in plan details, states ("has"/"no") and warnings,
  titles "Grant/Revoke mailbox permissions on", "Forward the mail of", "Stop forwarding the mail of", the external
  warning in the window's words, a non-security recipient by the admin center's name ("a distribution list");
  `BridgeDispatcher.cs`'s own usage hints say "enter"/"choose" with sign-in name, email address, object ID; the
  user search's empty-text hint ("enter the start of a name or an email address", `UserSearch.cs`,
  `SnapshotAccess.cs`). The group lookup's errors stay the command line's (window-side map instead).
- 2026-11-02 — the browser mock follows the core's new words and answers an ambiguous group ("Fin": Finance, Finance
  EU); tests: the quoted strings of 14 front-end test files, `vocabulary.test.ts` (new), the C# tests that assert the
  changed texts, `UsageGuideTests` (the two card titles), the bridge samples `plan.json`/`plan-applied.json`.
- 2026-11-02 — docs: `docs/usage.md` "The window" rewritten against the UI (the seven mismatches of the review: "Left
  as it is:" and "Nothing to do", the diff's part names and "Not compared", the group field's "one word of its name",
  the mailbox field's accepted values, the mailbox card's Details/Incomplete/failed lists, the picker's placeholder,
  the no-profile hint quoted from the card) and the CLI lines that name Full Access; `docs/development.md` "The front
  end" (the vocabulary paragraph, the renamed buttons and titles) and the mailbox writes' warning; README's two
  mentions of mailbox rights; `docs/images/window-group-members.png` retaken (Finance, light, 1280 px viewport, the
  card alone, 976 × 420 px, field not focused).

## Deviations from the plan

- The Core's plan texts are shared with the command line, so `--dry-run` and the plan records now read "Full
  Access", "has …", "Grant mailbox permissions on", "Forward the mail of", "Stop forwarding the mail of" too; no
  `M365Aegis.Core.Output` Verify snapshot quoted them (only test assertions changed), and the records written before
  keep their words (a resume matches by row and key, not by text). The command line still takes `FullAccess`.
- The group lookup's "no group named …" and "… names N groups" stay the command line's (its tests and its users need
  the object ids): the window rewords them in `windowError` (`vocabulary.ts`), as C-5 allowed.
- The forwarding confirmation's button is "Forward mail" (was "Start forwarding"), so the card's verb runs from the
  choice to the badge to the button.
- `windowWarning` makes an unknown warning a sentence (first letter, closing period) instead of passing it unchanged.

## Verification

- 2026-11-02 — .NET: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -c Release
  -p:SkipFrontendBuild=true` (0 warnings), `dotnet test` 1658 passed, the coverage gate 100 % lines and branches
  (Core 6881/3517, Cli 2159/550); `changelog.py --check`, `check-fixtures.py --self-test` (17 cases) and
  `check-fixtures.py` (540 files, 0 findings).
- 2026-11-02 — front end: `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (286 tests, 27
  files, 100 %), `npm run build`.
- 2026-11-02 — the mock on port 5177 (playwright-cli, light, 1280 px), read against the glossary, screenshots
  `122-*.png` in the round's scratchpad: Group members (Finance, "Fin" ambiguous, "Finanse" miss), Mailbox
  permissions (the shared list, Reception with Details, bob with Incomplete), User access (dave), Access changes
  (everyone, bob), Change mailbox permissions (plan, confirmation, result, a not-done result, a refusal for
  "archive"), Mailbox forwarding (outside address without a copy: plan and confirmation; Stop forwarding to the
  result), the sign-in card and the bar signed in, `?mock=signed-out`, `?mock=no-exchange`, `?mock=no-snapshot`, and
  the page in dark. No "(s)", no raw enum, no `.sqlite` name, no "FullAccess" in the window but the mock's own failed
  list line (a service message). `docs/usage.md` "The window" read against the UI once more at the end.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
