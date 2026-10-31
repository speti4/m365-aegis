---
size: M
origin: session 2026-10-31
created: 2026-10-31
started: 2026-10-31
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

1. [ ] `docs/setup.md` written; the CLI lines checked against Microsoft Learn
2. [ ] `docs/development.md`: the permissions list replaced by a link, the layout updated
3. [ ] `docs/usage.md` reframed, the trust text rewritten
4. [ ] `README.md` rewritten; the deleted paragraphs' facts checked present elsewhere
5. [ ] `SECURITY.md`, `.github/ISSUE_TEMPLATE/config.yml`, `CONTRIBUTING.md`
6. [ ] Verification: `dotnet test -c Release`, `python scripts/check-fixtures.py`, markdownlint, the stale-text grep,
       a read of `docs/setup.md` as a stranger

## Log

- 2026-10-31 — promoted from the backlog as the branch's first commit

## Deviations from the plan

- none yet

## Verification

- pending

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (a task session of a parallel-sessions repo: listed in the pull request under `## Follow-ups`, the main session files them)
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
