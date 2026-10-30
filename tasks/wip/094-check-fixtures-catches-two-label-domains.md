---
size: S
origin: wip/086-usage-guide-for-the-command-line-and-the-window.md
created: 2026-10-30
started: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 094 — `scripts/check-fixtures.py` catches two-label domains too

## Context

The `ANY_DOMAIN` pattern of the fixture scan asks for three labels, so `alice@<company>.com` or `<company>.hu`
passes the check today; the pattern accepts two labels, the allow-list names the domains the repo may carry
(`contoso.com`, `fabrikam.com`, Microsoft's endpoints, the package registries, the docs' links), and whatever the
tightened scan flags in the fixtures, the docs and the guide is cleaned or allow-listed with a reason.
Why: the repo is public (D-010) and the scan is the gate that keeps tenant data out; a two-label domain is the common
shape of a real tenant's domain.
Done when: a test fixture with a two-label domain fails the scan, the repo passes it with 0 findings, and the
allow-list's entries each carry a one-line reason.
Tenth round of 2026-10-31, on one branch with tasks 088 and 093, done first so the other bundles of the round meet the
tightened scan when they rebase; the task session owns the steps below and may reshape them.

## Docs that move

- `tests/fixtures/README.md`: the scan's rule (two labels, the allow-list and its reasons)
- `CONTRIBUTING.md`: the "Checks before a commit" line if the scan's invocation or its expectations change

## Not in this change

- A scan of the git history or of the GitHub issues (the mirror reads the task files, which the scan covers)
- The anonilm scan of the project-pal plugin: a maintainer's tool, not the repo's gate

## Steps

1. [x] The pattern: two or more labels, with the allow-list of domains the repo may carry, each with a one-line reason
2. [x] The scan over the repo: every new finding cleaned (a fixture or doc value rewritten to `contoso.com`) or
       allow-listed with its reason
3. [x] A self-test of the scan: a two-label domain in a fixture fails, the repo passes with 0 findings
4. [x] Docs under *Docs that move*; the Verification dated; `tasks_check` without `write` clean apart from the stale
       index

## Log

- 2026-10-31 — promoted on `task/088-doctor-and-fixture-scan` by the main session
- 2026-10-31 — the rule: `ANY_DOMAIN` is any name of two or more labels ending in one of `TOP_LEVEL_DOMAINS` (`com`,
  `net`, `org`, `hu`, `eu`, `de`, `at`, `ch`, `uk`, `io`, `dev`, `info`, `biz`, `co`, `ai`); a URL escape (`%40`) is
  taken out before the match, so the `@` of a fixture's file name does not glue onto the domain. A host passes when it
  is a made-up tenant's (`MADE_UP_TENANTS`: contoso, fabrikam, example; their `.com` with any subdomain, their
  `.onmicrosoft.com`, their SharePoint hosts) or on `ALLOWED_HOSTS`, a dict of host to its one-line reason, where
  `*.name` allows the name and its subdomains and a bare name only itself (`onmicrosoft.com`, `sharepoint.com` and
  `outlook.com` are bare, since a tenant's name sits under them). The GUID exceptions became `ALLOWED_GUIDS` with
  reasons in the same shape.
- 2026-10-31 — the scope: besides the fixture folders, the snapshots and `docs/usage.md`, the scan now reads every
  Markdown file at the root and under `docs/`, `tasks/` and `.github/` (the docs' links of the Context, and the task
  files the Not-in-this-change line counts on); the path-length rule stays on the fixture folders. 451 files.
- 2026-10-31 — the findings of the tightened scan and what became of them: `System.IO` in `tests/sidecar/sidecar.Tests.ps1`
  (9 lines; allow-listed as code), `40contoso.com` in a sign-ins fixture's file name (the URL escape, fixed in the
  rule), the two example hosts of this task's own Context (rewritten to `<company>`), and over the prose the
  Microsoft, GitHub, NuGet, PowerShell Gallery, DigiCert, research and library hosts (allow-listed, 24 entries, each
  used at least once). No tenant value was found.
- 2026-10-31 — `--self-test`: 16 lines with their expected verdict (a two-label domain, a `%40` address, another
  tenant's SharePoint, `.onmicrosoft.com` and `mail.protection.outlook.com` hosts, a host that only starts like
  Graph's, `evilmicrosoft.com`, a real GUID and a JWT are findings; the made-up names, the allowed hosts and file
  names are not) and a temp fixture with `alice@<company>.hu` that must give exactly its file, line and host; the
  `lint` job runs it before the scan.
- 2026-10-31 — `tests/fixtures/README.md` held its own text twice (a second copy from line 162 on, without the window
  bundle's section, since the merge of #89); the copy is removed and the rule is a new subsection under The rules.

## Deviations from the plan

- The scan's scope grew to the repo's Markdown (root, `docs/`, `tasks/`, `.github/`), which the Context's allow-list of
  the docs' links and the Not-in-this-change line about the task files assumed; the code and the front end are not
  read (their package lock carries the registries' hosts by the hundred and no tenant data).
- The duplicated second copy of `tests/fixtures/README.md` is removed in the same change.

## Verification

- 2026-10-31 — `python -I scripts/check-fixtures.py` before: 0 findings over the old scope with the three-label rule;
  with the new rule over the old scope and the prose: 12 findings (above), after the fixes: `451 files, 0 findings`.
  `python -I scripts/check-fixtures.py --self-test`: `17 cases, 0 misses`; with `contoso` taken out of
  `MADE_UP_TENANTS` as a mutation, the self-test fails (3 misses) and the scan fails (734 findings).

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
