---
status: decided
date: 2026-10-29
cost: cheap
sources: [wip/014-spike-photino-against-winforms-and-webview2.md, D-002-dotnet-10-core-cli-and-window-with-react.md, D-007-framework-dependent-readytorun-publish.md]
---

# D-009 — The window is a WinForms form with the WebView2 control; Photino.NET is removed

## Context

D-002 put the window on Photino.NET behind `IUiHost` and named a WinForms and WebView2 host as the fallback a
spike would compare, because Photino's upstream has been dormant since 2025-01. Task 014 built both hosts behind
`IUiHost` with the same React front end and bridge and measured them on 2026-10-29 on the maintainer's machine
(the numbers are in task 014's Log). Both pass the `--smoke` run, show the hello page, run the `--dev-url` hot
reload, carry the bridge on an http origin, keep the data folder under the home, and give a top-level window
handle for the broker. They differ on:

- **Page loading.** Photino loads the shipped page from `file://` only by starting WebView2 with
  `--allow-file-access-from-files`, a flag that lets any `file://` page read local files; a custom scheme
  handler left the page on `about:blank` in a short try. WinForms serves the page from an https virtual host
  with no browser flag.
- **The bridge.** Photino hands the host a bare string with no origin. WinForms exposes the whole `CoreWebView2`
  API: the message's source origin is checked, navigation off the page's origin is cancelled, new windows are
  refused.
- **Maintenance.** Photino.NET 4.0.16 and Photino.Native 4.0.22 were released 2025-01-23, with no code commit
  since; it ships net8.0 and net9.0 builds only. The WebView2 SDK ships a stable release about every four
  weeks and WinForms is part of .NET 10 LTS (supported to 2028-11).
- **Size and speed.** WinForms adds about 1.6 MB unpacked (0.3 MB zipped) and about 0.15 s to the first
  `ready` (527–543 ms against 364–382 ms warm); its browser processes linger about 6 s after the exe exits.
- **Runtime.** WinForms needs the .NET Desktop Runtime 10 next to the .NET Runtime 10; the command line keeps
  needing only the latter.

## Decision

- `m365-aegis-gui` hosts the front end in a WinForms form with the `Microsoft.Web.WebView2` control; Photino.NET,
  its package and its host are removed; `IUiHost` stays as the seam with one implementation.
- The GUI project targets `net10.0-windows` with `UseWindowsForms` and `EnableWindowsTargeting`, so the Linux CI
  job keeps building it; the Core and the Cli stay plain `net10.0`.
- The shipped `wwwroot` is served from `https://m365-aegis.example/` (`SetVirtualHostNameToFolderMapping`,
  access `Deny`); `--dev-url` loads the Vite server as given, with the developer tools on.
- The user data folder is `<home>\webview2` (D-005). The host accepts bridge messages only from the page's
  origin and keeps navigation on it; the bridge protocol and the front end do not change.
- The form's handle is the broker's parent window for the GUI sign-in (task 017).
- The release stays framework-dependent (D-007); its prerequisite for the window becomes the .NET Desktop
  Runtime 10, which the doctor and the README name.
- Cost: cheap, one class behind `IUiHost`. Decided 2026-10-29 by the maintainer on the spike's measurements.

## Not chosen

- **Photino.NET 4.0.16:** smaller, quicker to start and keeps the GUI on `net10.0`, but no release since 2025-01,
  `file://` loading behind a flag that weakens the browser, a bare-string bridge, and every message payload
  logged to stdout by default.
- **WPF with WebView2:** the same SDK in a heavier framework, for no gain with one control filling the window.
- **A plain Win32 window with `CoreWebView2Controller`:** no WinForms dependency, but the window, DPI and
  message-loop code would be ours to maintain.
- **WinUI 3 / Windows App SDK:** a second runtime to deploy.
