## What and why

<!-- What changes, and why. Link the task file (tasks/wip/NNN-….md) or the issue (Fixes #N). -->

## Follow-ups

<!-- Work this change found but did not do, one line each; the maintainer files the approved ones as tasks. "none" is fine. -->

## Checklist

- [ ] `dotnet restore --locked-mode`, `dotnet build -c Release -p:SkipFrontendBuild=true` and `dotnet test -c Release` pass
- [ ] `dotnet format --verify-no-changes` passes
- [ ] `python -I scripts/changelog.py --check` passes; a closed task brings its `CHANGELOG.md` line
- [ ] When `gui/web/` changed: `npm ci`, `npm run lint`, `npm run typecheck`, `npm test` and `npm run build` pass
- [ ] When `Directory.Packages.props`, a lock file, `gui/web/package.json` or `.github/` changed: `python -I scripts/check-vulnerable.py`,
      `dotnet nuget-license …` (CONTRIBUTING, "Checks before a commit"), `python -I scripts/npm-licenses.py` and `uvx zizmor .github` pass, or none changed
- [ ] No tenant data anywhere: fixtures, tests, screenshots and the description use made-up tenants only
      (`python -I scripts/check-fixtures.py` passes)
- [ ] A new package has its one-line reason in `Directory.Packages.props` (or `docs/development.md` for the front end), or there is none
- [ ] Every write goes through a plan and `--dry-run` / `--yes` or a confirmation in the window (D-006), or the change has no write
- [ ] The commits pass the hook (`git config core.hooksPath .githooks`)
