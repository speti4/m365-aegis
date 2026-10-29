---
status: idea
size: S
origin: done/014-spike-photino-against-winforms-and-webview2.md
created: 2026-10-29
milestone: M1 Foundation
---

# The WebView2 license override follows the package's version

What: `licenses/overrides.json` names the `Microsoft.Web.WebView2` package with one version, so each monthly
Dependabot bump fails the `deps` job until the line is edited by hand; either a version-free override, if
`nuget-license` accepts one, or the override generated from `Directory.Packages.props` by a script, or a note in
`dependabot.yml` that groups the bump with the edit.
Why: every failed `deps` run on a Dependabot pull request costs CI minutes (D-005) and a hand edit; the
broker's native package in the same file has the same shape.
Done when: a Dependabot bump of the WebView2 package passes the `deps` job without a hand edit, or the
mechanism that makes the edit is documented in `docs/development.md`, "CI and releases".
