---
status: waiting
waits-for: the first push to speti4/m365-aegis
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# GitHub repository settings by hand after the first push

What: the settings `docs/development.md` ("CI and releases") describes and no file carries: the ruleset on
`main` (pull request with squash merge only, the required checks `dotnet`, `deps`, `frontend`, `sidecar`,
`lint`, `workflow-lint`, `windows` and `commit-rules`, linear history, no force push or deletion), the ruleset
that keeps `v*` tags, squash as the only merge method with the pull request text as the message, private
vulnerability reporting, Dependabot security updates, secret scanning; the result recorded here.
Why: the workflow of `docs/git-workflow.md` holds only with these; they are repository settings, not files.
Done when: a pull request shows every check as required, a direct push to `main` is rejected, and the settings
are listed in this task's Verification.
