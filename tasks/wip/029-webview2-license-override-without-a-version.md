---
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M1 Foundation
---

# 029 — The WebView2 license override follows the package's version

## Context

`licenses/overrides.json` names the `Microsoft.Web.WebView2` package with one version, so each monthly
Dependabot bump fails the `deps` job until the line is edited by hand; either a version-free override, if
`nuget-license` accepts one, or the override generated from `Directory.Packages.props` by a script, or a note in
`dependabot.yml` that groups the bump with the edit.
Why: every failed `deps` run on a Dependabot pull request costs CI minutes (D-005) and a hand edit; the
broker's native package in the same file has the same shape.
Done when: a Dependabot bump of the WebView2 package passes the `deps` job without a hand edit, or the
mechanism that makes the edit is documented in `docs/development.md`, "CI and releases".
Last of the Window bundle (017+030, 027, 028, 031, 029) of the round of 2026-10-29; the task session owns the
steps below and may reshape them. A `.github/` or package change adds the dependency checks of CONTRIBUTING to
the local matrix.

## Docs that move

- `docs/development.md`, "CI and releases": the mechanism

## Not in this change

- the Dependabot schedule itself

## Steps

1. [x] Whether `nuget-license` accepts a version-free override; else a script that derives the overrides from
       `Directory.Packages.props` and runs in the `deps` job; else the Dependabot note
2. [x] Proven locally with the `deps` job's commands over a bumped version
3. [x] Docs

## Log

- 2026-10-29 — promoted by the main session into the Window bundle (017+030, 027, 028, 031, 029)
- 2026-10-29 — nuget-license 4.0.18's `--override-package-information` takes one exact version per entry: an entry
  without `Version`, with `*` or with a range (`[1.0.0,)`) stops the tool with "is not a valid NuGet version". Its
  `--licensefile-to-license-mappings` maps a package's license text to a license, with no version: the two texts,
  copied verbatim from the packages, are under `licenses/texts/`, `licenses/license-texts.json` maps them
  (`BSD-3-Clause`, `LicenseRef-Microsoft-Software-License-Terms`), and `licenses/overrides.json` is gone. The
  `deps` job, `release.ps1`'s notices and CONTRIBUTING's local command use the mapping (CONTRIBUTING's command had
  neither file and failed on the two packages). A props-derived script was not needed, and could not have covered
  the broker's native package, which is transitive.

## Deviations from the plan

- The mechanism is the tool's license-text mapping, none of the three the plan listed; a bump whose license text
  changes still fails the job, which is the review a changed license deserves (documented).

## Verification

- 2026-10-29 — the `deps` job's `nuget-license` command over a temporary bump (WebView2 1.0.4314.35 in
  `Directory.Packages.props`, NativeInterop pinned to 0.20.7, `dotnet restore` rewriting the lock files): with the
  old `overrides.json` exit 1 on the bumped packages; with `license-texts.json` exit 0. Without either, exit 1
  ("Unable to determine license from the given license file"). The bump was reverted and `restore --locked-mode`
  is clean.
- 2026-10-29 — the CONTRIBUTING dependency checks on the branch: `check-vulnerable.py`, `dotnet tool restore`,
  `nuget-license` in the `deps` job's and CONTRIBUTING's form, `npm-licenses.py`, `npm audit --omit=dev`,
  `uvx zizmor .github`; `release.ps1 -DryRun` writes the notices with the mapping.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
