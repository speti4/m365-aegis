---
size: S
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 107 — The setup and usage guides, the license and a readme in the release zip

## Context

The guides live in `docs/` on GitHub first (decided 2026-10-31, task 106 writes `docs/setup.md`), and the release zip
must carry the same files, so a colleague with only the download has the administrator's steps too. Today
`scripts/release.ps1` copies `docs/usage.md` as `GUIDE.md` and nothing else from `docs/`; the zip has no license file
and no readme.
Done when: a local `release.ps1` run without a certificate produces a zip whose file list has `GUIDE.md`, `SETUP.md`,
`LICENSE.txt` and `README.txt`, and the tests and lint checks pass.

## Docs that move

- `scripts/release.ps1`: copies `docs/setup.md` → `SETUP.md`, `LICENSE` → `LICENSE.txt`, writes `README.txt` (what the
  folder holds, the guides, the GitHub URL, how to verify the download); copies `docs/images/` when it exists
- `scripts/changelog.py` (`--notes`): the footer names the guides in the zip and the docs on GitHub
- `docs/development.md`, "The release": the zip's contents
- `SECURITY.md`: the zip's contents where it describes the download
- the test that covers the zip's file list, if one exists under `tests/` or `scripts/` (extend it; otherwise say so)

## Not in this change

- the guides' text (task 106); until 106 merges, the copy step tolerates a missing `docs/setup.md` only in a local
  run and fails in a release
- a tag or a release run: the next real release proves the change

## Steps

1. [x] `scripts/release.ps1`: the four files, the images folder when present
2. [x] `scripts/changelog.py --notes`: the footer
3. [x] `docs/development.md` and `SECURITY.md`
4. [x] The zip file-list test, if one exists
5. [x] Verification: a local `release.ps1 -Tag v0.1.0` run without a certificate, the zip's file list, `dotnet test`,
       the `lint` and `workflow-lint` checks locally

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit
- 2026-10-31 — `release.ps1`: one ordered map of the repo's file and its name in the zip (`docs\usage.md` →
  `GUIDE.md`, `docs\setup.md` → `SETUP.md`, `LICENSE` → `LICENSE.txt`), checked right after the certificate
  checks. A missing `docs\setup.md` is a warning in a local run (the zip goes without `SETUP.md`) and an error under
  `-RequireSignature`, which only the release job passes, so it fails before anything is built; the other two are
  always required. `-RequireSignature` was picked over `-Tag` or `-DryRun` because the `windows` job's signed dry run
  must stay green on this branch before task 106 merges, and a local `-Tag v0.1.0` run must work too.
- 2026-10-31 — `README.txt` is written by the script (plain text, CRLF, UTF-8 without BOM): the version, the GitHub
  URL, the folder's files (`SETUP.md` and `images\` only when packed), where the guides live on GitHub, the
  checksum and attestation checks, and the signature paragraph only when the zip is signed (otherwise "not signed",
  as the notes line). `docs\images\` is copied as `images\` at the zip's top level, next to the guides, so
  `images/<file>` links in `docs/setup.md` resolve unzipped.
- 2026-10-31 — no test covered the zip's file list (`tests/release/sign-release.Tests.ps1` covers the signing and
  the `-RequireSignature` refusals; there is no Python test of `changelog.py`). Instead of a Pester test that would
  need a whole publish, `release.ps1` now opens the packed zip and fails when an exe, `THIRD-PARTY-NOTICES.md`,
  `README.txt` or a guide is missing at its top level; the `windows` job's dry run runs that check on every pull
  request. A Pester test of the `docs\setup.md` refusal was left out: it holds only until 106 merges.
- 2026-10-31 — `changelog.py --notes`: the footer's last sentence moved into a new "In the zip" section naming
  `SETUP.md`, `GUIDE.md` (linked to `docs/setup.md`, `docs/usage.md`), `README.txt`, `LICENSE.txt` and the notices.
  `README.md` names only `GUIDE.md` and was left alone: task 106 rewrites it.
- 2026-10-31 — rebased onto `main` after #139 (task 106) merged, by the main session: `docs/setup.md` is on `main`
  now, so the tolerance of a missing one (the warning and the two `README.txt` branches) came out of `release.ps1`
  and `docs/development.md`: every guide and the license are required in every run. The README's documentation
  table names `SETUP.md`, `README.txt` and `LICENSE.txt` in the zip. Both follow-ups of the pull request are done here.

## Deviations from the plan

- none: the zip-content check sits in `release.ps1` rather than in a test, since no test of the zip existed (step 4)

## Verification

- 2026-10-31, in the worktree: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build
  -c Release -p:SkipFrontendBuild=true` pass; `dotnet test -c Release` 1533 passed, 0 failed;
  `changelog.py --check` up to date; `check-fixtures.py --self-test` 17 cases, 0 misses; `check-fixtures.py` 498
  files, 0 findings; `check-commit.sh --self-test` 38 cases; `uvx zizmor .github` no findings (the `lint` and
  `workflow-lint` jobs). `Invoke-Pester -Path tests/release`: 7 passed, 2 skipped (the elevated-session tests).
- 2026-10-31, `release.ps1 -Tag v0.1.0 -Out <scratch>` without a certificate, no `docs/setup.md` on the branch: the
  warning "no docs\setup.md in this checkout: the zip goes without SETUP.md", `docs: GUIDE.md, LICENSE.txt,
  README.txt`, 6.2 MB zip, 43 entries. The top level beside the 20 `.dll` files and `wwwroot/`: `GUIDE.md`,
  `LICENSE.txt`, `m365-aegis-gui.deps.json`, `m365-aegis-gui.exe`, `m365-aegis-gui.pdb`,
  `m365-aegis-gui.runtimeconfig.json`, `m365-aegis.deps.json`, `m365-aegis.exe`, `m365-aegis.pdb`,
  `m365-aegis.runtimeconfig.json`, `M365Aegis.Core.pdb`, `README.txt`, `runtimes/win-x64/`,
  `sidecar/aegis-exo-sidecar.ps1`, `THIRD-PARTY-NOTICES.md`.
- 2026-10-31, the same run signed with a throwaway certificate (`-CertificatePath … -TimestampUrl ''`) and a
  placeholder `docs/setup.md` and `docs/images/sample.png` put in the worktree for the run and removed after it:
  `docs: GUIDE.md, SETUP.md, LICENSE.txt, README.txt`; the zip adds `SETUP.md` and `images/sample.png`;
  `README.txt` lists `SETUP.md` and `images\` and carries the signature paragraph.
- 2026-10-31, `release.ps1 -DryRun -RequireSignature -CertificatePath …` without `docs/setup.md`: refused before the
  build with "missing for the zip: docs\setup.md (its SETUP.md)". The `windows` job's own command
  (`-DryRun -CertificatePath … -TimestampUrl ''`) without it: warns and passes.
- 2026-10-31, after the rebase onto `main` with `docs/setup.md` and `docs/images/` present and the tolerance removed:
  `release.ps1 -DryRun` printed `docs: GUIDE.md, SETUP.md, LICENSE.txt, README.txt`, 6.3 MB zip, the zip's file
  check passed; `check-fixtures.py` 499 files, 0 findings; `changelog.py --check` up to date; `Invoke-Pester
  tests/release` 7 passed, 2 skipped; `tasks_check` green with the index regenerated by the main session.
- 2026-10-31, `changelog.py --notes 0.1.0`: the "In the zip" section names `SETUP.md`, `GUIDE.md` with links to
  `docs/setup.md` and `docs/usage.md`, `README.txt`, `LICENSE.txt` and `THIRD-PARTY-NOTICES.md`.
- 2026-10-31, merged as #138 (`68350f7`) after #139; closed on `main` by the main session, no follow-up left.

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-31`
