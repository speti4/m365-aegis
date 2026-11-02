---
size: S
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
closed: 2026-11-02
plan: none
milestone: M4 Window
---

# 126 — Small flow fixes in the window

## Context

A bundle of one-line fixes. Done on a write card either clears the fields or usage.md stops saying it starts the
card over; a failed result states its summary once, the code in a details line, and offers "See the plan again"; a
sign-in in progress gets a Cancel instead of a sixteen-minute wait; the lookup button keeps its width while it reads
"Looking up…"; the technical details (the Read through column and its sentence, "answered in", object ids, error
codes) go behind a collapsed Details line; the diff card's fields get a format hint (`latest`, `yesterday`, a date)
and the user picker; table headers smaller and muted, tabular numbers, a fixed column layout for the diff tables.
Why: the UX/UI review of 2026-11-02 (findings U-9, U-10, U-12, D-12, U-11, D-10, C-12, D-8): none is worth a task
alone, together they are the texture a support colleague meets every day.
Decided 2026-11-02: bundled with task 125 on one branch. The labels of the diff card's fields came with task 120;
the result's button is "Start a new change" since task 120, so U-9 is about what that button keeps.
Done when: each item is ticked with its check in the mock, and usage.md says what "Start a new change" does.

## Docs that move

- `docs/usage.md`, "The window": what "Start a new change" keeps, "See the plan again", the Cancel of a sign-in, the
  Details line, the diff card's format hint

## Not in this change

- The vocabulary (task 122); the type scale, the page tint and the change ticket (task 127) — the table header and
  tabular-number tweaks here are the data tables' own, not the scale
- A cancel message in the bridge protocol: the page stops waiting on its own; the host's sign-in is left to finish
  or time out

## Steps

1. [x] U-9: decide what "Start a new change" keeps (recommended: the mailbox, nothing else) and align the test and
       usage.md
2. [x] U-10: the failed result once (no repeated summary), the error code in a details line, "See the plan again"
       beside "Start a new change" (a new plan with what is still missing)
3. [x] U-12: a Cancel on the sign-in card while a sign-in is in progress (device code or broker) that drops the
       waiting state to signed out; a late reply is ignored
4. [x] D-12: the lookup button keeps its width ("Look up" → "Looking up…" with a min width or an inline spinner)
5. [x] U-11 / D-10: the Read through column and its layer sentence, "answered in …", the object id column and the
       raw error codes behind a collapsed Details (`<details>`) per answer
6. [x] C-12: the diff card's From / To fields get a helper line ("Leave empty for the latest two; `latest`,
       `yesterday` or a date such as 2026-10-31 work too") and the user field uses the picker
7. [x] D-8: `ui/table.tsx` headers smaller and muted, `tabular-nums` on counts and times, `table-fixed` with a
       shared column layout for the diff tables
8. [x] Tests for each behavior change; docs under *Docs that move*; the front-end checks; the mock walked through

## Log

- 2026-11-02 — promoted on `task/125-126-write-card-picker-and-small-fixes` by the main session, with task 125
- 2026-11-02 — U-9 decided as recommended: "Start a new change" keeps the mailbox and resets the rest (users or
  address, rights, Grant / Forward, the boxes) through the card's `onStartOver`; "Cancel" on the confirmation still
  keeps every pick. The test that wanted the fields kept now checks the reset (`mailbox-write-cards.test.tsx`,
  `applied-writes.test.tsx`)
- 2026-11-02 — U-10: `ResultView` says "N of M change(s) were made." once; the host's summary (its sentence and hint
  repeat the list and the plan record) is left out but for its code under "Details", and its sentence shows only when
  no change carries a message of its own; "See the plan again" (primary, "Start a new change" outlined) after an apply
  not done in full sends the same write request again (`request-plan` allowed from the result), so the host plans
  only what is still missing
- 2026-11-02 — U-12: a "Cancel" on the sign-in card while a sign-in waits; the reducer's `cancel` goes back to signed
  out with `canceled` (a "stopped waiting" line, the status "Sign-in canceled; signed out"), and `App` counts its
  sign-ins so a late reply is dropped; no bridge message
- 2026-11-02 — D-12: `SubmitButton` sits in a one-cell grid with an invisible, `aria-hidden` copy of its working
  label, so it is as wide as the longer label (measured in the mock: 99 px idle and busy)
- 2026-11-02 — U-11 / D-10: `DetailsLine` (`components/details-line.tsx`, a closed `<details>`); the mailbox access
  card's "Read through" column, its layer sentence and "answered in", the group members' Id column (now the group's
  and each member's id), and every error code (`ErrorText`) moved under it
- 2026-11-02 — C-12: the access changes card's From / To get a line under the fields (their description), "Only this
  user" is the user picker (`enterPicksFirst`; a pick fills, Enter on a closed list compares); the fields sit in a
  top-aligned grid with the button under them, so the open list moves nothing
- 2026-11-02 — D-8: `ui/table.tsx` headers `text-xs text-muted-foreground`, the table `tabular-nums`; counts and times
  in the cards' lines `tabular-nums`; the diff tables `table-fixed` with one `colgroup` and wrapping cells
- 2026-11-02 — rebased onto `main` after task 127 (#181): kept its `h-8` header and row heights and its `font-mono`
  address cells beside this change's header classes and dropped columns; `npm ci` (Geist removed), the front-end checks
  again green (commits `710c62e`, `a67a60a`)

- 2026-11-02 — merged as #182 (squash); closed on `main` by the main session; no follow-up (the README picture
  is retaken by task 122, which rewrites the same card's strings)

## Deviations from the plan

- U-9: "Start a new change" also sets Grant / Forward and the boxes back to their start, not only the users and rights
- U-12: a late reply is dropped even when it is a successful sign-in (the page said it stopped waiting); the card's
  line says Refresh reads the state again
- D-12: an invisible sizer instead of a spinner or a fixed minimum width, so the width follows the font of task 127
- U-11 / D-10: the snapshot file names inside the diff's warnings stay (they are the core's sentences, worded by the
  wording map); not among this task's steps

## Verification

- 2026-11-02 — `npm ci`, `npm run lint`, `npm run typecheck`, `npm run test:coverage` (277 tests, 100 % on all four
  counters), `npm run build`; `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build`
  (0 warnings), `dotnet test` (1658 tests; one full run green, one with the known load-dependent
  `AChildThatHangsTimesOutWithItsLastStderrLinesAndIsKilledOnDispose` failure of task 128, which passed alone),
  `changelog.py --check`, `check-fixtures.py --self-test` and the scan (0 findings)
- 2026-11-02 — the browser mock walked with Playwright (port 5176): Reception with Send on Behalf for alice and bob →
  "Not done", "0 of 2 change(s) were made." once, "Details" open shows "Error code: partial", the record path once;
  "See the plan again" → "Plan: 2 changes" (both still missing); a done grant, "Start a new change" → the mailbox kept,
  Users empty, the focus in the mailbox field; Mailbox access "finance": "Look up" 99 px idle and busy, "Details"
  closed then opened with Enter (the layers, "Answered in"), dark mode; Group members' ids under "Details"; Access
  changes: the helper line, "bob" picked with Enter, Enter compared, the tables' columns aligned; `?mock=signed-out`:
  Sign in → "Cancel" → "Sign-in canceled; signed out", the mock's late reply ignored; screenshots `125-126-*.png` in
  the session scratchpad

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-11-02`
