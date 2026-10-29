---
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
started: 2026-10-29
plan: none
milestone: M6 Release and team
---

# 031 — No reference documentation files in the publish

## Context

`PublishReferencesDocumentationFiles=false` in the publish of both exes (`scripts/release.ps1` or the
project files), so the XML documentation files of the referenced packages stay out of the zip.
Why: the WebView2 package's XML files alone add 639 KB unpacked to the release of 2026-10-29 (9.6 MB), and
nobody reads them there.
Done when: `scripts/release.ps1 -DryRun` lists no `*.xml` next to the assemblies and the size in
`docs/development.md`, "The release", is updated.
Part of the Window bundle (017+030, 027, 028, 031, 029) of the round of 2026-10-29; the task session owns the
steps below and may reshape them.

## Docs that move

- `docs/development.md`, "The release": the size

## Not in this change

- trimming or single-file publish (D-007 stands)

## Steps

1. [x] The property in the publish of both exes (the session picks the place)
2. [x] `scripts/release.ps1 -DryRun` lists no `*.xml` next to the assemblies; the size in the docs

## Log

- 2026-10-29 — promoted by the main session into the Window bundle (017+030, 027, 028, 031, 029)
- 2026-10-29 — in `scripts/release.ps1`, where the other publish flags live (the project files say so):
  `-p:PublishDocumentationFile=false` too, since our own `M365Aegis.Core.xml`, `m365-aegis.xml` and
  `m365-aegis-gui.xml` (287 KB) shipped as well; the script now fails when any `*.xml` is left in the publish
  folder, so the dry run of every pull request holds it.

## Deviations from the plan

- Our own documentation files are dropped along with the packages' (`PublishDocumentationFile=false`), which the
  task did not name: they are as useless in the zip.

## Verification

- 2026-10-29 — `scripts/release.ps1 -DryRun`: passes the new no-XML guard; 9.8 MB unpacked, 4 MB zipped, with the
  window's first page and the toast packages in it. The XML files the flags keep out add up to 919 KB in the build
  output (WebView2 Core and WinForms 639 KB, `M365Aegis.Core.xml` 250 KB, the two exes' 31 KB).

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green on the branch (a stale index is expected), the file stays in `wip/`; the main session
      closes it on `main` after the merge
