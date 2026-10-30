---
size: S
origin: done/040-window-sign-in-over-the-bridge.md
created: 2026-10-30
started: 2026-10-30
plan: none
closed: 2026-10-30
milestone: M4 Window
---

# 046 — The window's sign-in failures get their own hints

## Context

The failure hints of `SignInFlow` name the command line (`run m365-aegis login again`, the `login --tenant …` line
when no profile exists) and the window's sign-in card shows them as they are; the window gets hints written for a
person in front of the card ("Sign in again", "ask the administrator for the tenant profile"), chosen by the host
(`IUiHost`) or by a hint set per surface, with the command line's text unchanged.
Why: a support colleague reads the card, not a shell (D-001); a hint that names a command they cannot run is noise.
Done when: every failure the Core tests cover shows a window hint in the card and the command-line snapshots are
unchanged.
Part of the Exchange and window follow-ups bundle (050, 049, 048, 046, 047) of the round of 2026-10-30; the task
session owns the steps below and may reshape them. The window's live check needs the maintainer at the picker and
runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, "The front end": where the window's hints live and how a surface picks its set

## Not in this change

- new failure kinds or a change of the command line's hint texts
- the first real task card (after the colleague's review of 018 and the M3 plan)

## Steps

1. [x] The hint set per surface (command line, window) and how the sign-in flow or the host chooses it
2. [x] The window's hints for every failure the Core tests cover; the card shows them
3. [x] Core tests over both sets; the command-line snapshots unchanged; the bridge contract samples if a payload changes
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window follow-ups bundle (050, 049, 048, 046, 047)
- 2026-10-30 — `Auth/SignInHints.cs`: a record with the eight hints the sign-in and the profiles end in (no silent
  sign-in, the picker cancelled, the picker failed, the device code failed, no profile or none of that name, no
  default, a file that does not parse, a profile without its values) and two sets, `CommandLine` (the old texts word
  for word) and `Window` (the card's buttons, the administrator for the profile). `AuthService` takes a set (the
  command line's by default) and hands it to `SignInFlow.RunAsync` and its `ProfileStore`; `BridgeServices.ForWindow`
  makes the window's service with `SignInHints.Window`, so the surface picks its set where it makes its sign-in and
  nothing reads strings back.
- 2026-10-30 — the card shows the hint under every signed-out state ("What to do: …"), no longer only without a
  profile; the `signed-out` sample carries the window's hint, which the bridge tests now dispatch over the window's
  set; the payload's shape is unchanged.

- 2026-10-30 — closed by the main session on `main` after #23; the approved follow-ups of the pull request are filed as backlog items 055–062

## Deviations from the plan

- the sign-in's own failures only: Graph's and the sidecar's hints that name a command (`m365-aegis login`, `doctor`)
  still reach the window as they are; a follow-up

## Verification

- 2026-10-30 — Core tests (`SignInHintsTests`): each of the nine failures (four of the flow, five of the profiles)
  carries the command line's old hint word for word and the window's own, with the same kind and sentence; no window
  hint names `m365-aegis`, a backtick or a `--` flag; the window's service gives its hint to `whoami` and to a silent
  token. The bridge tests hold `signed-out`, a login without a profile and a cancelled picker to the window's hints.
  The command line's snapshots unchanged. `dotnet test`: 645 passed; Vitest 57 passed.
- 2026-10-30, live (the window from this branch, the maintainer in front of it): with an empty home the card shows
  "signed out", "There is no tenant profile on this computer yet…" and "What to do: ask the administrator to set up
  the tenant profile on this computer; the window signs in with it", no Sign in button. With a temporary home whose
  profile names an account the broker does not hold: "What to do: choose Sign in on the sign-in card to sign in
  again"; Sign in, then Cancel in the Windows account picker: the toast's hint "choose Sign in again and pick the
  account in the Windows sign-in window".

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [x] moved to `done/` with `closed: 2026-10-30` (the main session, on `main` after the merge)
