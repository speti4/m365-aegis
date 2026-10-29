---
status: idea
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
milestone: M6 Release and team
---

# No reference documentation files in the publish

What: `PublishReferencesDocumentationFiles=false` in the publish of both exes (`scripts/release.ps1` or the
project files), so the XML documentation files of the referenced packages stay out of the zip.
Why: the WebView2 package's XML files alone add 639 KB unpacked to the release of 2026-10-29 (9.6 MB), and
nobody reads them there.
Done when: `scripts/release.ps1 -DryRun` lists no `*.xml` next to the assemblies and the size in
`docs/development.md`, "The release", is updated.
