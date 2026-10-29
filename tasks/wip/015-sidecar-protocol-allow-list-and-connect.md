---
size: M
origin: session 2026-10-28
created: 2026-10-28
started: 2026-10-29
plan: none
milestone: M2 Exchange
---

# 015 — The sidecar in the core: the process, the protocol client, the allow-list, connect and reconnect

## Context

`Exchange/Sidecar` in the core starts `pwsh -NoProfile -NonInteractive -File sidecar\aegis-exo-sidecar.ps1`,
speaks the JSON-lines protocol the scaffold's script and its Pester tests define (`hello`, `connect`, `invoke`,
`exit`, the `error` replies), passes the broker's token (task 009) and renews it with a `reconnect` before the
expiry, maps a cmdlet's objects to typed records, and turns `cmdlet-not-allowed` and `cmdlet-failed` into the
exit codes of D-006; the allow-list is the one place a cmdlet is admitted. The window keeps one sidecar alive;
the command line starts one per run (a named-pipe daemon is a later idea).
Why: every mailbox command of M2 rides on this; the script exists, the core side does not.
Done when: the Core tests drive a real pwsh child against the fake module on Linux and Windows, a refused
cmdlet and a failed cmdlet come back as envelopes with the right codes, and `mailbox list` answers live.
Core of the Exchange bundle (009, 015, 016, 011, 010) of the round of 2026-10-29; the task session owns the
steps below and may reshape them. A new exit code's number is handed out by the main session. The live check
runs in the main session's signed-in round.

## Docs that move

- `docs/development.md`, the Exchange Online sidecar: the process, the protocol, the allow-list, the exit codes
- `CONTRIBUTING.md`, "Checks before a commit": the sidecar line if the test layout changes
- `tests/sidecar/` README or the fixtures README: the fake module's data

## Not in this change

- the mailbox commands beyond `mailbox list` (task 016)
- a named-pipe daemon for the command line
- the Admin API backend (task 010)

## Steps

1. [x] The process host and the protocol client in `Core/Exchange/Sidecar`, the C# allow-list as the one place
       a cmdlet is admitted (the script's list derived from or checked against it), typed records
2. [x] `connect` with the broker's token and `reconnect` before the expiry; the fallback only if task 009's live
       run fails
3. [x] `cmdlet-not-allowed` and `cmdlet-failed` as exit codes of D-006 (numbers from the main session)
4. [x] The Core tests drive a real pwsh child against the fake module (Linux and Windows CI jobs); the Pester
       tests follow the script's changes; the doctor's sidecar hash follows
5. [x] `mailbox list` as the first command, appended at the end of the command list in `Cli/Program.cs`
6. [x] Live check (main session): `mailbox list` after one sign-in
7. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Exchange bundle (009, 015, 016, 011, 010)
- 2026-10-29 — built: `Exchange/Sidecar/` in the Core (`SidecarClient`: the pwsh child and the protocol, one
  request in flight, the reply matched by id, the last 20 stderr lines kept scrubbed; `ExchangeSession`: start on
  the first cmdlet, connect with the broker's token, `reconnect` with a fresh token five minutes before
  `ExpiresOn`, a dead sidecar replaced on the next cmdlet, safe for the window to hold; `ExoCmdlets`: the one
  allow-list; `ExoCommand`/`ExoObject`: a cmdlet run and its objects as plain values; `SidecarFailures`: the error
  replies as exit codes; `ExchangeConnection`: pwsh from PATH, the script checked against the build's hash, the
  token, the organization). The script grew `reconnect`, `properties` and `input` (piped values) on `invoke`, the
  `errors` list of a bulk run, raw UTF-8 stdin and stdout, and scrubs every token it was handed (and anything
  JWT-shaped) from the message, the error id and the category. Exit codes 11 `sidecar-unavailable`, 12
  `cmdlet-not-allowed`, 13 `cmdlet-failed` (the numbers from the main session); a refused or expired token is 3,
  a 403 or a missing role 8, a missing object 4, throttling 5. `mailbox list [--shared]` in the Cli through
  `ExchangeRunner` (the `GraphRunner` shape), appended at the end of the command list. 382 .NET tests and 20
  Pester tests green
- 2026-10-29 — after the live round: a not-found answer carried the service's whole REST error (a JSON body and
  the name of the server the object was looked for on, several hundred characters). `SidecarFailures.Shorten`
  now keeps the body's one sentence without the server, cut at 300 characters, for every mapped failure; a
  mailbox that is not found is `mailbox '<as given>' not found`; the whole text goes to the log under
  `--verbose` only (scrubbed like every entry). 388 .NET tests green

## Deviations from the plan

- The script keeps its own copy of the allow-list as a second guard, held equal to `ExoCmdlets.Allowed` by a Core
  test and compared with the list `hello` reports, rather than receiving the list from the core.
- The session asks for the token and the organization before it starts pwsh, so a missing sign-in is exit code
  3 without a child process.
- Two hidden commands joined for the live round: `probe exchange-connect` (task 009) and `probe admin-api` (task
  010), and `M365AEGIS_EXO_CONNECT` switches the connect's parameters without a rebuild.

## Verification

- 2026-10-29 — locally, on Windows: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, the
  Release build with the front end (0 warnings), `dotnet test` 388 green (the Core and Cli tests start the sidecar
  as a real pwsh child against the fake module), `Invoke-Pester -Path tests/sidecar` 20 green, PSScriptAnalyzer
  over the three folders CI checks 0 findings, `changelog.py --check`, `check-fixtures.py` (136 files, 0
  findings); with an empty `M365AEGIS_HOME` `doctor --offline` exited 0 (`sidecar-script` passes on the changed
  script), `whoami` 3, `mailbox list` 3; the window's `--smoke` 0; `release.ps1 -DryRun` green. The new test data
  went through the anonymizer scan, which flagged only the made-up contoso, fabrikam and example addresses
- 2026-10-29 — against the tenant (620 mailboxes), in the main session's signed-in round (evening, UTC+1) from
  this branch's Release build, ExchangeOnlineManagement 3.10.1, pwsh 7.6.9: the sidecar connected with the
  broker's token at the first try (task 009); `mailbox list` exit 0 in 5419 ms, 620 mailboxes (381 user, 219
  shared, 18 room, 2 scheduling); `mailbox list --shared` exit 0 in 4390 ms, 219. The not-found message was cut
  to one sentence after this round (Log); the code and the hint were right
- 2026-10-29 — re-checked live from the rebuilt exe (around 22:20, UTC+1): `mailbox access <no such address>`
  exit 4 in 3482 ms, `not-found`, the message `mailbox '<the address as given>' not found` and the hint naming
  `mailbox list`

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
