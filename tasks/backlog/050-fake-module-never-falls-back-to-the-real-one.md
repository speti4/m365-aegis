---
status: idea
size: S
origin: done/042-admin-api-backend.md
created: 2026-10-30
milestone: M2 Exchange
---

# The sidecar tests never fall back to the machine's real Exchange Online module

What: when the fake module under `tests/sidecar/modules/` lacks a cmdlet of the allow-list, pwsh's module
auto-loading imports the machine's real `ExchangeOnlineManagement` for that one name; the script sets
`$PSModuleAutoLoadingPreference = 'None'` when `M365AEGIS_EXO_MODULE_ROOT` points at a test module, and a test holds
the fake module's exports equal to the allow-list, so a missing export fails the test instead of reaching the
real module.
Why: on 2026-10-30 a new allow-list cmdlet missing from the fake's manifest made a test call the real module (it
failed unconnected and nothing reached a tenant, but the guard was luck, not design).
Done when: a cmdlet removed from the fake's exports fails the sidecar tests with a message naming it, and
`Invoke-Pester -Path tests/sidecar` stays green on a machine without the real module.
