---
size: S
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
changelog: housekeeping
---

# 075 — A home folder ACL test: the MSAL cache, the profiles and the plan records readable by the user only

## Context

A Windows-only test that creates the home folder through the Core (`AegisHome`), writes the MSAL cache, a
`config.json` and a plan record through the product's own paths, and reads each file's ACL: no access entry for
`Everyone`, `Users` or `Authenticated Users`, the owner and `SYSTEM`/`Administrators` only; the cache's DPAPI
protection asserted by reading the file as bytes and finding no token marker in clear.
Why: SECURITY.md names a cache readable by another user as a vulnerability; `%LOCALAPPDATA%` inherits a user-only
ACL on a standard machine, but a folder created elsewhere through `M365AEGIS_HOME` does not.
Done when: the test is green on the `windows` job and red when the home folder is created with an inherited
`Users: read` entry.
Part of the safety tests bundle (073, 071, 074, 072, 075) of the sixth round of 2026-10-30; the task session owns
the steps below and may reshape them. If the test shows that `AegisHome` does not set the ACL on a folder created
under `M365AEGIS_HOME`, the fix (the folder created with an explicit user-only ACL) is part of this task and the
docs name the rule.

## Docs that move

- `docs/development.md`, the home folder section: the ACL rule (what the product sets, what the test holds)
- `SECURITY.md` only if its scope sentence changes (else none)

## Not in this change

- The MSAL cache's own encryption (DPAPI through `Microsoft.Identity.Client.Extensions.Msal`, asserted, not changed)
- A Linux or macOS home folder mode

## Steps

1. [ ] The Windows-only test in the Core tests (skipped on Linux with a reason), the home created through the
       product under a temp `M365AEGIS_HOME`, the three files written through the product's paths, the ACL read
       with `System.Security.AccessControl`
2. [ ] The DPAPI assertion over the cache bytes with a marker token
3. [ ] The red proof: a home created with an inherited `Users: read` entry fails the test (a Log line)
4. [ ] The fix in `AegisHome` if the product does not set the ACL; the docs rule

## Log

- 2026-10-30 — promoted by the main session into the safety tests bundle (071–075)

## Deviations from the plan

- none yet

## Verification

- (pending)

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
