---
status: ready
size: M
origin: session 2026-10-30
created: 2026-10-30
milestone: M3 Writes with safety
---

# The `Plan` type, the plan records and the write guard

What: a `Plan` in the Core (a title, the changes as current → desired per object, what is already so, the
warnings; the shape the 018 sketch's made-up plan mimics, so the first task card can consume it later), rendered as
a table and inside the envelope by `M365Aegis.Core.Output`; every write command gets `--dry-run` (prints the plan,
exit 0) and `--yes` (applies); without either the plan is printed and the exit is 7 (D-006); an apply writes a plan
record (the plan, each change's result, the timestamps) under `%LOCALAPPDATA%\m365-aegis\plans\` (`AegisHome`,
`M365AEGIS_HOME` for the tests); a partial apply reports per change and exits with a code the main session
assigns; no `--force`. The `call` guard's ad-hoc `{method,url,body}` plan becomes a `Plan` if it fits.
Why: M3 starts here; every write of 052–054 is a plan first, and the window's cards confirm the same plan.
Done when: a fake write in the tests shows the plan under `--dry-run`, refuses with exit 7 without a flag, applies
under `--yes` with a record file in a temp home, and the Cli snapshots hold the three renderings.
