---
size: S
origin: session 2026-11-02
created: 2026-11-02
started: 2026-11-02
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

1. [ ] U-9: decide what "Start a new change" keeps (recommended: the mailbox, nothing else) and align the test and
       usage.md
2. [ ] U-10: the failed result once (no repeated summary), the error code in a details line, "See the plan again"
       beside "Start a new change" (a new plan with what is still missing)
3. [ ] U-12: a Cancel on the sign-in card while a sign-in is in progress (device code or broker) that drops the
       waiting state to signed out; a late reply is ignored
4. [ ] D-12: the lookup button keeps its width ("Look up" → "Looking up…" with a min width or an inline spinner)
5. [ ] U-11 / D-10: the Read through column and its layer sentence, "answered in …", the object id column and the
       raw error codes behind a collapsed Details (`<details>`) per answer
6. [ ] C-12: the diff card's From / To fields get a helper line ("Leave empty for the latest two; `latest`,
       `yesterday` or a date such as 2026-10-31 work too") and the user field uses the picker
7. [ ] D-8: `ui/table.tsx` headers smaller and muted, `tabular-nums` on counts and times, `table-fixed` with a
       shared column layout for the diff tables
8. [ ] Tests for each behavior change; docs under *Docs that move*; the front-end checks; the mock walked through

## Log

- 2026-11-02 — promoted on `task/125-126-write-card-picker-and-small-fixes` by the main session, with task 125

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
