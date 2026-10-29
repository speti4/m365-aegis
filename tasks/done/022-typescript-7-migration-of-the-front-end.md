---
size: S
origin: session 2026-10-29
created: 2026-10-29
started: 2026-10-29
closed: 2026-10-29
plan: none
milestone: M1 Foundation
changelog: housekeeping
---

# 022 — TypeScript 7 migration of the front end

## Context

The scaffold (task 001) kept the front end on TypeScript 5.9.3 while the 7.x native compiler settled. On
2026-10-29 Dependabot's pull request #2 (5.9.3 → 7.0.2) failed `npm run typecheck` with TS5102: `baseUrl` was
removed from the compiler options. The maintainer decided 2026-10-29 to migrate now rather than ignore the
major: `gui/web` moves to 7.0.2, the `@/*` path mapping is rewritten without `baseUrl` (Vite's `resolve.alias`
already resolves it at build time), the `tsc -b --noEmit` scripts, the Vitest and Biome runs and the Vite build
are checked under 7, and the reason line in `docs/development.md` is updated. Done when `npm run lint`,
`typecheck`, `test` and `build` pass on 7.0.2 locally and in CI, and pull request #2 is closed as superseded.
Offline work; no tenant.

## Docs that move

- `docs/development.md`, "The front end": the `typescript` reason line (the 5.x clause goes; what 7 changed for
  the repo, one clause).
- `CONTRIBUTING.md` only if a front-end check command changes (expected: none).

## Not in this change

- Any other front-end package bump (Vite, Vitest, Biome, React stay as they are unless 7 forces one; then the
  reason is in the Log).
- The window's first real page (017) or any UI change.

## Steps

1. [x] `typescript` 7.0.2 in `gui/web/package.json`, `npm install` regenerates `package-lock.json`
2. [x] `tsconfig.json`: `baseUrl` removed, `paths` kept relative to the config (`"@/*": ["./src/*"]`); any other
       option 7 dropped or renamed handled from the compiler's own messages (none: 7.0.2 reports only `baseUrl`)
3. [x] `tsc -b --noEmit` under 7 still checks the sources (a probe file with a type error fails the `typecheck`
       script) and resolves `@/lib/utils` through `paths` alone, so the `build` and `typecheck` scripts stay
4. [x] `npm run lint`, `npm run typecheck`, `npm test`, `npm run build` green; `dotnet build -c Release` (with the
       front end, the `BuildFrontend` target forced once with a deleted `dist/`) and `m365-aegis-gui --smoke`
       still pass
5. [x] `python -I scripts/npm-licenses.py` and `npm audit --omit=dev` green (the native compiler's platform
       packages are dev-only and Apache-2.0, nothing new on `licenses/allowed.json`); the `.NET` side of the
       package-change block (`check-vulnerable.py`, `nuget-license`) green as well
6. [x] `docs/development.md` updated; `CONTRIBUTING.md` unchanged (no check command changed)
7. [x] Verification written; the pull request names #2 as superseded

## Log

- 2026-10-29 — promoted by the main session; the work runs in the worktree on this branch
- 2026-10-29 — `typescript` 7.0.2 in `package.json`; `npm install` regenerated `package-lock.json`: the one
  version line plus the `@typescript/typescript-<os>-<cpu>` optional dev packages of the native compiler
  (Apache-2.0, never shipped). `npm run typecheck` on the unchanged `tsconfig.json` failed with TS5102 (`baseUrl`
  removed), the same error as Dependabot's #2; after deleting the `baseUrl` line it passed with no further
  message, so no other option of this config was dropped or renamed. A probe file (`src/zz-probe.ts`, deleted
  again) with a wrong assignment and an `@/lib/utils` import made `tsc -b --noEmit` exit 1 with TS2322 and
  TS6133 and no TS2307, so `-b --noEmit` checks under 7 as it did under 5.9 and `paths` resolves the alias
  without `baseUrl`; the `build` and `typecheck` scripts stay as they are.
- 2026-10-29 — `npm run lint` (Biome 2.6.3, 13 files, clean), `npm test` (Vitest, 1 file, 6 tests), `npm run
  build` (18 modules, `dist/assets/index-*.js` 222.01 kB), `python -I scripts/npm-licenses.py` (20 shipped
  packages, 0 outside the allowlist), `npm audit --omit=dev` (0 vulnerabilities), `dotnet restore --locked-mode`
  and `dotnet build -c Release` (0 warnings, 0 errors), `dotnet test` (44 passed), `m365-aegis-gui --smoke`
  exit 0 with `M365AEGIS_HOME` on a temp folder, `scripts/check-vulnerable.py` (0), `nuget-license` (0 outside
  the allowlist), `scripts/changelog.py --check` and `scripts/check-fixtures.py` (16 files, 0 findings).
- 2026-10-29 — Vite 8 prints a config-loader warning about `__dirname` in `vite.config.ts` on `test` and
  `build`; it predates this change and is not TypeScript's, so it is a follow-up, not part of this change.
- 2026-10-29 — the one follow-up (`__dirname` → `import.meta.dirname` in `gui/web/vite.config.ts`, the Vite 8
  warning about the `native` config loader) was done in this same pull request by the main session, so no
  backlog item was filed; lint, typecheck, tests and build green without the warning

## Deviations from the plan

- none: the `build` and `typecheck` scripts, `CONTRIBUTING.md` and `licenses/allowed.json` needed no change

## Verification

- 2026-10-29 — in the worktree on `task/022-typescript-7-migration-of-the-front-end`: `gui/web/package.json`
  pins `typescript` 7.0.2 and `npx tsc --version` prints `Version 7.0.2`; `tsconfig.json` has no `baseUrl` and
  keeps `"paths": {"@/*": ["./src/*"]}`; `npm run lint`, `npm run typecheck`, `npm test` (6 tests) and `npm
  run build` exit 0; `dotnet build -c Release` with the `BuildFrontend` target running (`dist/` deleted first)
  exits 0, `dotnet test` 44 passed, `m365-aegis-gui --smoke` exit 0; `npm-licenses.py` 20 shipped packages,
  0 outside the allowlist; `npm audit --omit=dev` 0 vulnerabilities; `tasks_check` without `write` reports only
  the stale index; CI on the pull request is recorded there.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`; the main session files the approved ones
- [x] every doc under *Docs that move* changed in this change (`docs/development.md`; `CONTRIBUTING.md` had no
      command to change)
- [x] Verification written, dated
- [x] `tasks_check` green (without `write`; the stale index is the main session's)
- [x] moved to `done/` with `closed: 2026-10-29`
