---
status: ready
size: S
origin: session 2026-10-30
created: 2026-10-30
milestone: M6 Release and team
---

# A home folder ACL test: the MSAL cache, the profiles and the plan records readable by the user only

What: a Windows-only test that creates the home folder through the Core (`AegisHome`), writes the MSAL cache, a
`config.json` and a plan record through the product's own paths, and reads each file's ACL: no access entry for
`Everyone`, `Users` or `Authenticated Users`, the owner and `SYSTEM`/`Administrators` only; the cache's DPAPI
protection asserted by reading the file as bytes and finding no token marker in clear.
Why: SECURITY.md names a cache readable by another user as a vulnerability; `%LOCALAPPDATA%` inherits a user-only
ACL on a standard machine, but a folder created elsewhere through `M365AEGIS_HOME` does not.
Done when: the test is green on the `windows` job and red when the home folder is created with an inherited
`Users: read` entry.
