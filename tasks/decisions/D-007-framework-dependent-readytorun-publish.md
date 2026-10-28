---
status: decided
date: 2026-10-28
cost: cheap
sources: [wip/001-project-scaffolding.md, D-002-dotnet-10-core-cli-and-window-with-react.md, D-004-exchange-online-in-three-layers-with-a-pwsh-sidecar.md]
---

# D-007 — Framework-dependent, ReadyToRun, win-x64; both exes in one folder; not single-file, not self-contained

## Context

The release is a zip the maintainer and later the support team unpack. The tool needs PowerShell 7.6 and the
Exchange Online module on the machine anyway (D-004), the Windows account manager's native library and
Photino's native library travel with the exes, and the .NET runtime receives monthly security fixes.

## Decision

`scripts/release.ps1` publishes `M365Aegis.Cli` and `M365Aegis.Gui` **framework-dependent for `win-x64` with
ReadyToRun**, into **one folder** (the shared assemblies once; central package management keeps their versions
equal), and zips it: the two exes, the assemblies, `wwwroot\`, `sidecar\aegis-exo-sidecar.ps1`,
`THIRD-PARTY-NOTICES.md` and `GUIDE.md`. The .NET 10 runtime is Windows Update's to patch, so a runtime fix needs
no release of ours; a missing runtime makes the apphost print the download link. WebView2 is preinstalled on
Windows 11 (the Evergreen runtime); Photino's data folder is set under the tool's home. Cost: cheap, a publish
setting. Decided 2026-10-28.

## Not chosen

- Self-contained: about 70 MB unpacked, and every runtime CVE would mean a release of ours.
- Single-file: complicates the native libraries (MSAL's `msalruntime`, Photino) that must sit on disk.
- NativeAOT: the broker breaks in a single file and PowerShell hosting cannot be compiled ahead of time.
- Two folders, one per exe: the shared assemblies twice and two places to patch.
