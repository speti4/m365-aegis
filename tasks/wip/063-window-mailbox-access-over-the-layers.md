---
size: S
origin: done/056-window-mailbox-list-through-the-admin-api.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M4 Window
---

# 063 — The window's `mailbox-access` over the layers

## Context

The window's `mailbox-access` request answers the way the command line's `mailbox access` does since task 042:
the mailbox and its Send on Behalf delegates with their display names from the Admin API, FullAccess and Send As
from the warm sidecar session, side by side over the one Exchange token the window holds since task 056.
Why: parity with the command line; today the window's card reads everything through the sidecar and shows the
delegates without their names.
Done when: the window's access card shows the same rows as `mailbox access <mailbox>`, the `window:` log line
names both layers, and the bridge tests hold it over the fixtures and the fake module.
The one task of the window bundle of the sixth round of 2026-10-30; the task session owns the steps below and
may reshape them. The writes follow-ups bundle (064–066) runs in parallel on `GroupWrites`, `PlanResume` and
`PlanStore`, and the safety tests bundle (071–075) on the tests; this task stays in `Core/Bridge`,
`Core/Exchange` reads it reuses and `gui/web`. The live check is the main session's: it starts the window from
the worktree's build, the maintainer opens the access card, the `window:` lines and the card's rows are compared
with `mailbox access <mailbox>` (counts and shapes only).

## Docs that move

- `docs/development.md`, the window and bridge section: the `mailbox-access` request's layers
- `README.md`: the bundle's own heading
- `tests/contracts/bridge`: the request and reply samples, read by the C# and the Vitest tests

## Not in this change

- A write from the access card (M4's plan-and-confirm card is its own task)
- The window's `mailbox-list` (task 056) and the hints (task 057)

## Steps

1. [x] The bridge handler runs the two layers the way `MailboxLookups` does for the command line, over the window's
       Exchange token source: `mailbox-access` with a mailbox calls the layered `MailboxLookups.AccessAsync(backends,
       …)`; the core's result names the layer of the mailbox and of every grant; the `window:` log line names what
       each layer read
2. [x] The front end's access card shows the rows with the display names and the layer; the contract sample, the
       browser mock and an Admin API fixture for the mailbox by its address
3. [x] Tests: C# over the fixtures and the fake module (the bridge, the layered read, the command line's `--json`);
       Vitest over the samples and the card's helpers
4. [x] Docs; the live steps for the main session in the report
5. [x] Live check (main session): the access card's rows and the `window:` line against `mailbox access <mailbox>
       --json`, counts and shapes only

## Log

- 2026-10-30 — promoted by the main session as the window bundle of the sixth round
- 2026-10-30 — the core's layered read already existed (task 042: `MailboxLookups.AccessAsync(ExchangeBackends, …)`,
  the Admin API's mailbox with the delegates by name beside the sidecar's FullAccess and Send As, the sidecar alone
  as the fallback); the window called the sidecar-only overload on `backends.Sidecar`. `BridgeDispatcher` now calls
  the layered one for `{"mailbox": …}` over the `ExchangeBackends` the window's `WindowExchange` gives each request
  (task 056: the warm session as the sidecar, an Admin API client over the window's one `ExchangeTokenSource`).
  `{"user": …}`, the reverse lookup, stays on the session: it is the sidecar's bulk read on the command line too, and
  the window's backends start no extra sidecars for its parallel read.
- 2026-10-30 — the layer of a row is not in `meta.backend` (which says `admin-api+pwsh` after a fallback too, the
  Admin API having been asked), so the core's result names it: `MailboxAccess.MailboxLayer` (`admin-api`, or `pwsh`
  when the run has no Admin API or it could not answer) and `MailboxHolder.Layer` (`admin-api` for a Send on Behalf
  delegate the Admin API listed, `pwsh` for FullAccess, Send As and every grant of a fallback), both init properties
  set by the layered read only (`Exchange/MailboxResults.cs`, `MailboxReads.cs`); no signature changed. The command
  line's `--json` of `mailbox access <mailbox>` carries them too; its table and the sidecar-only lookup's envelope
  snapshot are unchanged. `MailboxWrites` reads the access through the same call and compares rights and names only,
  so a plan is unchanged.
- 2026-10-30 — the `window:` line of a mailbox's access names what each layer read, by counts, never a name
  (`BridgeDispatcher.DescribeLayers`): `mailbox-access answered in … ms through admin-api+pwsh (the mailbox and 1 Send
  on Behalf delegate(s) from admin-api, 2 FullAccess and Send As grant(s) from pwsh); exchange session 1, sidecar
  starts 1, …`; after a fallback `(the Admin API could not answer: the mailbox, … from pwsh)`, without an Admin API
  `(no Admin API in this window: …)`. A fallback also writes the core's own `admin-api:` line with the service's code.
- 2026-10-30 — front end: `protocol.ts` reads `mailboxLayer` and each holder's `layer` (optional, one of `admin-api`,
  `pwsh`; anything else refuses the reply, as an unknown right does); the access card has a "Read through" column
  (Admin API or PowerShell) and a line under the mailbox on what each layer read (`describeLayers`); the browser mock
  answers Finance exactly as the sample and bob's mailbox through the session alone, as the recorded Admin API
  refuses it. The bridge sample `mailbox-holders.json` is now the Admin API answer (the window's product wiring);
  the request sample asks by address, as the card's pick does, so the Admin API fixture
  `Mailbox@Identity=finance@contoso.com&IncludeGrantSendOnBehalfToWithDisplayNames=true` joins (Finance's body,
  request id `…0a1`).
- 2026-10-30 — tests: `BridgeProtocolTests` (the access over both layers is the sample on one sidecar start and one
  token acquisition; a window without an Admin API and a holder made without layers answer the sample with every
  layer `pwsh`; the Admin API's 403 for bob falls back to the session; the Admin API's 404 for nobody is `not-found`
  with the window's hint; the log text by counts; the 056 test renamed for the access over both layers),
  `MailboxReadsTests` (the layers of the Admin API read, of the fallback, and of the sidecar-only read, which names
  none), `MailboxLayersCommandTests` (the command line's `--json` carries the layers); Vitest: the sample's layers,
  a reply without layers, an unknown layer refused, the card's `layerNames` and `describeLayers`, the mock's Finance
  equal to the sample.
- 2026-10-30 — the main session's live round: the window's access over both layers matched `mailbox access
  <mailbox> --json` by right and layer on a shared mailbox with delegates; the warm lookup answered in 384 ms
  (Verification)

## Deviations from the plan

- The layer of a row is a field of the core's result, not of the bridge reply alone: the command line's `--json`
  carries `mailboxLayer` and `layer` too (additive; its table, the snapshots and the plans unchanged), so the card's
  rows and `mailbox access <mailbox> --json` compare one to one.
- A user's access (the reverse lookup) stays on the session, as on the command line; only the mailbox direction
  moved to the layers.
- One Admin API fixture joins (Finance by its address) with a section of its own in `tests/fixtures/README.md`,
  which *Docs that move* did not name.

## Verification

- 2026-10-30, local (the worktree, the full matrix of CONTRIBUTING): `dotnet restore --locked-mode`, `dotnet format
  --verify-no-changes --no-restore` exit 0, `dotnet build --no-restore -c Release` with the front end (0 warnings),
  `dotnet test --no-build -c Release` 813 passed (the 5 new and 6 changed cases of this task included; no
  `*.received.*`, the snapshots unchanged), `changelog.py --check` up to date, `check-fixtures.py` 286 files 0
  findings; `npm ci`, `npm run lint`, `npm run typecheck`, Vitest 63 passed (6 new), `npm run build`; the `windows`
  job's steps with an empty `M365AEGIS_HOME`: `--json doctor --offline` 0, `--json whoami` 3, `m365-aegis-gui
  --smoke` 0 (ready after 871 ms, top-level window, no sidecar started), `scripts/release.ps1 -DryRun` 0. The
  sidecar script and the fake module are untouched, so no Pester run.
- 2026-10-30, live (the main session's signed-in round; the worktree's Release build of the window with `--verbose`,
  the real home folder, no fixture or test variable, the maintainer at the window; nothing written). An interactive
  sign-in through the picker (the silent attempts failed with the broker's usual state after a logout), then the
  shared mailboxes (`window: mailbox-list answered in 9380 ms through admin-api; exchange no sidecar, token
  acquisitions 1`), then three `mailbox-access` lookups, each `through admin-api+pwsh`: a first mailbox on the cold
  sidecar in 10266 ms (0 Send on Behalf delegates from admin-api, 2 FullAccess and Send As grants from pwsh; session 1,
  sidecar starts 1, last connect 1785 ms, token acquisitions 1); a shared mailbox with delegates in 3761 ms (5 from
  admin-api, 10 from pwsh), and the same mailbox again, warm, in 384 ms with the same counts, still one session, one
  sidecar start and one token acquisition. One `exchange-online: exchange token acquisition 1 … valid for 73 min`
  line for the whole run; no `admin-api: the Admin API could not read the mailbox` line, so no fallback ran.
- 2026-10-30, live, the command line on the same shared mailbox (`mailbox access <address> --json`, the worktree's
  build, its own process and sidecar): exit 0 in 5870 ms wall (`meta.elapsedMs` 5528), `meta.backend`
  `admin-api+pwsh`, `data.mailboxLayer` `admin-api`, 15 holders: Send on Behalf 5 on `admin-api`, FullAccess 7 and
  Send As 3 on `pwsh`. These are the same 5 and 10 the window's line counted. Every Send on Behalf holder carries a
  display name that differs from its address, and an address (5 of 5); the holder keys are `address`, `holder`,
  `layer`, `right`. The card showed that mailbox's lookup; the maintainer checks the "Read through" badges and the
  line under the mailbox at the window, and the main session confirms them in the pull request.
- 2026-10-30, live, observed: a user lookup afterwards (the reverse lookup, unchanged by this task) answered `through
  pwsh` in 261886 ms on the same session (sidecar starts 1, token acquisitions 1): the session's one-sidecar bulk
  read over every mailbox, as before this task. The window starts no extra sidecars for task 043's split read;
  that is a follow-up candidate in the pull request.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
