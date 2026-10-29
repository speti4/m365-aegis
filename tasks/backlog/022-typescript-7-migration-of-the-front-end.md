---
status: ready
size: S
origin: session 2026-10-29
created: 2026-10-29
milestone: M1 Foundation
---

# TypeScript 7 migration of the front end

What: move `gui/web` from TypeScript 5.9.3 to 7.0.2 (the native compiler): the Dependabot pull request #2 fails
`npm run typecheck` with TS5102, `baseUrl` was removed from the compiler options, so the `@/*` path mapping is
rewritten without it (Vite's alias already resolves it); check the `tsc -b --noEmit` scripts, the Vitest and
Biome runs and the Vite build under 7; update the reason line in `docs/development.md` ("The front end") that
kept the 5.x line while 7 settled.
Why: 7 is out and Dependabot will keep proposing it; the migration is small while the front end is one page.
Done when: `npm run lint`, `typecheck`, `test` and `build` pass on 7.0.2 locally and in CI, and the Dependabot
pull request #2 is closed as superseded.
