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

1. [ ] The glossary as code: a `vocabulary.ts` (or extend `plan-wording.ts`) with the display-name maps (mailbox
       types, group kinds, member types, sharing scopes, calendar roles, the action verbs), `plural(n, noun)` and
       the existing `sentence()` moved there; every card and `plan-view.tsx` reads them
2. [ ] The write flow's words: Choose › Review › Confirm › Result, "Review the plan", "Continue", "Back to edit";
       the plan badge Grant / Revoke / Forward / Stop forwarding; "Check before you confirm"; the states
       "No Full Access" → "Has Full Access"; the Core's forwarding titles and the warnings in the window's words
       (`MailboxWrites.cs`; the CLI snapshots that quote them updated)
3. [ ] The read cards' words: titles "Mailbox permissions" / "Change mailbox permissions", "Permission | User or
       group | Email address" headers, the empty states, the counts, the card descriptions as one plain line each,
       the placeholders and labels (sign-in name / email address / object ID; a site column URL), sentence-case
       badges, the diff's "How" values, the snapshot named by time
4. [ ] Errors: the toast and `LookupError` texts through `sentence()`; the bridge's hints say "enter"/"choose";
       the ambiguous-group hint names the groups, not their ids (`GroupLookups.cs`, `BridgeDispatcher.cs`;
       the CLI's own hints untouched where the text is shared — a window-side rewording map is acceptable)
5. [ ] Tests, docs under *Docs that move*, the README picture; the front-end checks; the .NET checks with the
       coverage gate if C# changed; a read-through of the mock in every state against the glossary

## Log

- 2026-11-02 — promoted on `task/122-window-vocabulary` by the main session

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
