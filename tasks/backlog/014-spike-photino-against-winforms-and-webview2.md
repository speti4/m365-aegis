---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Spike: Photino.NET 4.0.16 on .NET 10 against a WinForms and WebView2 host, recorded as a decision

What: a timeboxed comparison (two days) behind `IUiHost`: the scaffold's Photino host against a WinForms window
hosting `Microsoft.Web.WebView2` with the same React front end and bridge; ES modules from a `file://` page
(Chromium refuses them: a custom scheme or a virtual host mapping), the dev loop with `--dev-url` and hot reload,
the bridge on an http origin, the WebView2 data folder under the home, the window handle for the broker (task
013), the size of the publish and the maintenance state of each. The pick becomes a decision (`/tasks decide`)
and the loser's host is deleted.
Why: Photino's upstream has been dormant since 2025-01 (D-002 names the risk); the window of M4 should stand on
a host that is maintained.
Done when: both hosts pass the `--smoke` run and show the hello page, the decision file states the pick with the
measurements, and `IUiHost` has one implementation again.
