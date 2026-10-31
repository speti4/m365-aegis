---
size: M
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M6 Release and team
---

# 106 — A user-facing README and a setup guide for another tenant's administrator

## Context

The repo is public with release 0.1.0 out, and the audit of 2026-10-31 found every onboarding fact present but scattered
over a contributor doc, the README and the usage guide, with no administrator's side in the shipped guide; the README is
a development log (task numbers, milestones, measurements) rather than a landing page, and four sentences still say
there is no release. Decided 2026-10-31: the guides live in `docs/` in the repo and the README links them (a GitHub
Pages site may be generated from them later); the self-signed signing certificate stays team-only (D-012 unchanged) and
the docs say that the checksum and the attestation are an outsider's trust path and the `signature` warning is expected.
Done when: `UsageGuideTests` pass, the fixture scan passes, and a reader with only the README and `docs/setup.md` can
register the app, consent, sign in and run a lookup in their own tenant.

## Docs that move

- `README.md`: rewritten as the landing page: pitch, badges (`ci`, release, MIT), what it does with one terminal sample
  (and one window screenshot from the mock transport under `docs/images/` if the session can capture one, otherwise
  none), install in five lines, "Set up your tenant" linking `docs/setup.md`, "Use it" linking `docs/usage.md`, a
  documentation table, status, license; no task numbers, milestones or measurements; every deleted paragraph's facts
  checked present in `docs/development.md` or `docs/usage.md` first
- `docs/setup.md` (new): for the administrator: who can register and consent; the registration (single-tenant public
  client, "Mobile and desktop applications" with `ms-appx-web://microsoft.aad.brokerplugin/<application id>` and
  `http://localhost`, "Allow public client flows", no credential; the portal path in words and the CLI equivalent
  verified against Microsoft Learn); the one canonical permissions table (command group · delegated permission · API ·
  role; read-only minimum, writes, snapshots; where the Office 365 Exchange Online API is in the portal); admin
  consent and the message-trace service principal; the first `login` and `whoami`; handing the zip and the three
  values to the team; the `signature` row, SmartScreen and Smart App Control outside the team; troubleshooting
- `docs/usage.md` (path unchanged: `UsageGuideTests` and `scripts/release.ps1` read it): the user's guide, linking
  `docs/setup.md`; "Check the download" without "the maintainer hands it out"; every `m365-aegis <path>`, every
  `### <card>` line and at least 40 runnable examples kept
- `docs/development.md`: "The sign-in and the home folder" keeps the design narrative and links the permissions table
  instead of listing it; "Layout" lists `docs/setup.md` (and `docs/images/` if added)
- `SECURITY.md`: no "no release yet"
- `.github/ISSUE_TEMPLATE/config.yml`: the advisory link points at this repo, not `speti4/mrgl`
- `CONTRIBUTING.md`: a paragraph naming the guides and their audiences

## Not in this change

- the release zip's contents (task 107)
- the tenant's size figures (task 105)
- a GitHub Pages site, a per-permission consent check in the doctor, a registration script
- the repository's topics and description on GitHub (the maintainer sets them after the merge)
- the release certificate's distribution (D-012 stands)

## Steps

1. [x] `docs/setup.md` written; the CLI lines checked against Microsoft Learn
2. [x] `docs/development.md`: the permissions list replaced by a link, the layout updated
3. [x] `docs/usage.md` reframed, the trust text rewritten
4. [x] `README.md` rewritten; the deleted paragraphs' facts checked present elsewhere
5. [x] `SECURITY.md`, `.github/ISSUE_TEMPLATE/config.yml`, `CONTRIBUTING.md`
6. [x] Verification: `dotnet test -c Release`, `python scripts/check-fixtures.py`, markdownlint, the stale-text grep,
       a read of `docs/setup.md` as a stranger

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit
- 2026-10-31 — `docs/setup.md` written: who registers and consents, the registration in the portal and with the Azure
  CLI, the one permissions table (read-only, writes, snapshots), the message trace service principal, the first sign-in
  and the tenant value, handing it to the team, the signature, SmartScreen and Smart App Control outside the team,
  troubleshooting. Checked on Microsoft Learn the same day: `az ad app create` / `update` (`--sign-in-audience
  AzureADMyOrg`, `--is-fallback-public-client`, `--public-client-redirect-uris`), `az ad app permission add`
  (`<id>=Scope`, the ids from `az ad sp show --id <resource app id>`), `az ad app permission admin-consent`,
  `az ad sp create`, `New-MgServicePrincipal -AppId` for the message trace, the "APIs my organization uses" > Office
  365 Exchange Online path for `Exchange.ManageV2`, Exchange Online's app id `00000002-0000-0ff1-ce00-000000000000`,
  and the roles that grant tenant-wide consent for delegated permissions
- 2026-10-31 — `docs/development.md`'s permission list became a link to the table, its three design facts kept in the
  narrative; `docs/usage.md` reframed as the user's guide with the trust text per D-012, and three facts of the old
  README moved there (`trace details` by Message-ID, the `--limit` continuation line, the distribution groups' writes
  through Exchange Online); every other paragraph of the old README checked present in `docs/development.md` before
  it went
- 2026-10-31 — README rewritten as the landing page; `SECURITY.md` (no "no release yet", writes exist now), the issue
  template's advisory link, a "guides and who reads them" section in `CONTRIBUTING.md`
- 2026-10-31 — rebased onto `main` after #137 by the main session (the index and the rewritten README resolved), the
  README's license line corrected (the zip ships Microsoft components, named in `THIRD-PARTY-NOTICES.md`); merged as
  #139 (`c246e03`). Closed on `main` by the main session. The follow-ups of the pull request, decided by the
  maintainer on 2026-10-31: the live check of the Azure CLI block and of the message trace role is backlog 108; the
  two stale snapshot sentences of `docs/development.md` were fixed in the closing commit; the doctor's per-permission
  consent check is backlog 109 (idea)

## Deviations from the plan

- `scripts/check-fixtures.py`: Microsoft Graph's and Office 365 Exchange Online's app ids (the same in every tenant,
  which the setup guide's Azure CLI lines name) on `ALLOWED_GUIDS`, and `img.shields.io` (the README's badges) on
  `ALLOWED_HOSTS`, each with its reason, as the scan's rule asks of a new host in a doc
- the usage guide links the setup guide by its GitHub address, not a relative path: the zip ships the usage guide alone
  as `GUIDE.md` until task 107 changes the zip

## Verification

- 2026-10-31, in the task's worktree: `dotnet restore --locked-mode`, `dotnet build -c Release
  -p:SkipFrontendBuild=true` and `dotnet format --verify-no-changes --no-restore` pass; `dotnet test --no-build -c
  Release` passes, 1533 tests, 0 failed, the Core and the Cli projects both run (`UsageGuideTests` among them: every
  command path, every card title and every example of the reworked `docs/usage.md` hold)
- 2026-10-31: `python -I scripts/check-fixtures.py --self-test` (17 cases, 0 misses) and `python -I
  scripts/check-fixtures.py` (499 files, 0 findings) with the new allow-list entries; `python -I scripts/changelog.py
  --check` up to date; for the `.github/` change `uvx zizmor .github` (no findings), `python -I
  scripts/check-vulnerable.py` (0) and `python -I scripts/npm-licenses.py` (0 outside the allowlist); no package
  changed, so no nuget-license run
- 2026-10-31: CI runs no markdownlint, so a scratch script checked every relative link and `#anchor` of `README.md`,
  `docs/setup.md`, `docs/usage.md`, `docs/development.md`, `CONTRIBUTING.md` and `SECURITY.md`: 0 broken. The
  stale-text grep (`no release yet`, `nothing is released`, `once there is a release`, `under construction`, `there is
  no release`, `mrgl`, `hands it out`) over the README, `SECURITY.md`, the guides, `CONTRIBUTING.md` and the issue
  template finds nothing; the README has no task number, milestone or measurement; no home-folder path in any changed
  file
- 2026-10-31: `docs/setup.md` read top to bottom as an administrator of a new tenant with only the README beside it:
  the registration, the permissions, the consent, the message trace step, `login`, `whoami`, `doctor` and a lookup
  follow without another file. The Azure CLI block is checked against the Learn reference, not run against a tenant
  (no signed-in round in this session)
- 2026-10-31: the window picture `docs/images/window-group-members.png` taken with playwright-cli from `npm run dev
  --prefix gui/web` (the browser mock, made-up data only), cropped to the Group members card

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-31`
