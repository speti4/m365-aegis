---
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M2 Exchange
---

# 049 — One Exchange token source for the sidecar and the Admin API

## Context

A command that uses both layers (`mailbox access` since task 042) fetches the Exchange token twice, once for the
sidecar's connect and once for the Admin API client, through two parallel silent broker calls; one shared source
(`ExchangeBackends` or the `AuthService`) hands the same token and its expiry to both, and the renewal before the
expiry happens once.
Why: two silent calls per command are twice the broker's cache round trip and two log entries for one fact; the
window's long-lived session (task 045) makes the duplication permanent.
Done when: a `mailbox access` run shows one Exchange token acquisition in the verbose log, the sidecar's
`reconnect` and the Admin API's token refresh read the same expiry, and the tests hold the count.
Part of the Exchange and window follow-ups bundle (050, 049, 048, 046, 047) of the round of 2026-10-30; the task
session owns the steps below and may reshape them. The live count comes from the main session's signed-in round.
The parallel M3 bundle (051–054) adds write methods to the Exchange layer; `ExchangeBackends` is a shared surface,
so the change here stays small and names itself in the pull request.

## Docs that move

- `docs/development.md`, "The Exchange Online sidecar": one token source for the layers, where the renewal happens

## Not in this change

- the window's session lifetime (045) or a token for Graph (its own scope, its own cache entry)

## Steps

1. [x] The shared token source (token + expiry, one silent call, one renewal) behind both layers
2. [x] The sidecar's `reconnect` and the Admin API's refresh read the same expiry
3. [x] Core tests with the fake broker holding the acquisition count; the verbose log line
4. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the Exchange and window follow-ups bundle (050, 049, 048, 046, 047)
- 2026-10-30 — `Exchange/ExchangeTokenSource.cs`: one `ExchangeToken` (the token, its expiry, the account, the tenant)
  per command, one acquisition however many layers ask at once, renewed when it is within `RenewBefore` (five
  minutes) of its expiry; a failed acquisition is not kept. `ExchangeSessionOptions.RenewToken` is the source's
  forced renewal for an explicit reconnect (the window's keep-warm timer, the probe), so the other layers get the
  fresh token too; the session options made over a source take its `Clock` and `RenewBefore`, so the sidecar's
  `reconnect` and the Admin API read the same expiry. The Admin API's private token cache is gone; its target (the
  tenant id, the account) comes from the source's token.
- 2026-10-30 — `ExchangeRunner` makes one source per run and hands it to the session, the Admin API and the extra
  sidecars, which now share the session's options (so the organization, a Graph call when the profile's tenant is
  not a `.onmicrosoft.com` name, is asked for once instead of once per sidecar). Under `--verbose` each acquisition
  writes `exchange-online: exchange token acquisition N: one token for every layer of the command, valid for M min`.
  `ExchangeBackends` is unchanged; its constructor takes the same parts.

## Deviations from the plan

- the extra sidecars of a split read share the source and the organization too (they asked for both once each before)

## Verification

- 2026-10-30 — Core tests (`ExchangeTokenSourceTests`): three layers asking at once make one acquisition; a token is
  kept until it is due and renewed once; an explicit renewal fetches again and a failed one is not kept; over
  `AuthService` and the fake broker, three asks make one `silent:alice@contoso.com` call; `mailbox access finance`
  over the sidecar (fake module) and the Admin API (fixtures) makes 1 acquisition; a due token makes the session's
  reconnect and the Admin API share the second acquisition, and the session's `RenewAsync` makes the third.
  `dotnet test`: 645 passed, 0 failed.
- 2026-10-30, live (the main session's signed-in round, the broker's cached token): `mailbox access <a shared mailbox>
  --json --verbose` exit 0, backend `admin-api+pwsh`, one pwsh child, 7.1 s; the run's log has exactly one
  `exchange token acquisition 1: … valid for 75 min` line and no `acquisition 2` (5 MSAL lines name
  `outlook.office365.com`). `mailbox list`, `mailbox list --shared` and `mailbox size --top 5` each logged one
  acquisition too. The window (`--verbose`, the real home): the shared list and one mailbox's access over one sidecar,
  exactly one `exchange token acquisition 1 … valid for 73 min` line, `window: mailbox-list answered in 4289 ms;
  exchange session 1, sidecar starts 1, renewals 0, last connect 504 ms`, then `mailbox-access answered in 2041 ms`
  on the same sidecar.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
