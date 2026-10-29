---
status: idea
size: S
origin: done/002-auth-with-msal-and-the-windows-account-manager.md
created: 2026-10-29
milestone: M1 Foundation
---

# `profile list`, `profile use` and `profile remove` over the tenant profiles of `config.json`

What: a `profile` command group in the command line: `list` prints the profiles with the default marked,
`use <name>` moves the default, `remove <name>` deletes a profile (and signs its account out first). Task 002
writes a profile only through `login --tenant … --client-id … --account …` and picks one with `--profile`.
Why: a second tenant (a lab tenant, a customer's) or a second account in the same tenant needs the file edited
by hand today; the commands keep the file's shape the Core's.
Done when: the three commands answer through the output contract with tests over a temporary home, and
`docs/development.md`'s sign-in section names them.
