---
status: decided
date: 2026-10-28
cost: one-way
sources: [wip/001-project-scaffolding.md, docs/research/2026-10-28-m365-admin-tooling-landscape.md, D-001-build-a-thin-personal-ops-layer-not-a-product.md]
---

# D-002 — .NET 10 and C#: a core library, a System.CommandLine command line and a Photino window with a React front end

## Context

D-001 builds a core, a command line and a window. The stack comparison of 2026-10-28 scored Rust with Tauri (the
maintainer's proven pattern), .NET 10, Go and a PowerShell module. Two facts decided it: full Exchange Online
reach needs the `ExchangeOnlineManagement` module in every stack, so the module does not separate them; and the
sign-in on Windows is where the stacks differ most, because the tool is used by an administrator and a support
team on Entra-joined machines under Conditional Access.

## Decision

- **.NET 10 and C#** for the core (`src/M365Aegis.Core`), the command line (`src/M365Aegis.Cli`, the exe
  `m365-aegis`, `System.CommandLine` 2.0) and the window (`src/M365Aegis.Gui`, the exe `m365-aegis-gui`). MSAL.NET
  with the Windows account manager gives single sign-on, Conditional Access support and a protected cache with no
  OAuth code of our own (D-003); Microsoft documents hosting the Exchange Online module from C# and
  `Connect-ExchangeOnline -AccessToken` takes the token the host acquired (D-004); the ecosystem (signing, Intune,
  the Graph and Exchange teams' own samples) is .NET.
- **The window is a web view hosting a React front end** (`gui/web`: Vite, React, TypeScript, Tailwind,
  shadcn/ui), reached through a small message bridge to the core. The host is Photino.NET today, behind an
  `IUiHost` interface, because Photino's upstream has been dormant since 2025-01; a WinForms and WebView2 host is
  the fallback a spike compares (task 014). The front end, the bridge protocol and the core do not change when
  the host does.
- Windows 11 first. The core and the command line target plain `net10.0`, so the tests run on Linux and a later
  port starts from a core that builds off Windows.
- The cost is one-way: the stack shapes every project. Decided 2026-10-28.

## Not chosen

- Rust with Tauri 2 (the mboxer and mrgl pattern): no MSAL and no broker (`azure_identity` 1.0 ships no delegated
  credential, the Windows account manager would be a WinRT interop spike), `graph-rs-sdk` stale, and the Exchange
  module a sidecar anyway; the 10 ms start-up and the small exe do not outweigh a hand-rolled sign-in for a tool
  the team uses.
- Go: delegated credentials exist in `azidentity`, the broker does not; a new language for the maintainer and
  the same sidecar.
- A PowerShell module with a GUI: the import tax it was meant to remove, and a fragile WPF-in-PowerShell or a
  licensed portal for the team.
- Node: the start-up cost and Electron for the window.
- WPF or Avalonia for the window: WPF locks the GUI to Windows, Avalonia has no shadcn/ui; a web view keeps the
  UI portable and the component library the maintainer asked for.
