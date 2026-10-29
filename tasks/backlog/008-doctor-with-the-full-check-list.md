---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# `doctor` with the full check list: the module version, WebView2, the sidecar hash, the sign-in, the endpoints

What: the scaffold's offline doctor gains the rest: the `ExchangeOnlineManagement` version through a pwsh call
(3.10 or later), the WebView2 runtime from the registry (`pv` under the EdgeUpdate client key), the sidecar
script's SHA-256 against the one compiled into the core, the sign-in state (the cached account and the token
expiry for Graph and Exchange), the device join state from `dsregcmd /status` (joined, registered or neither, and
a primary refresh token or not, which explains a failed silent operating-system-account sign-in; task 013) and
the reachability of the three endpoints; `--offline` keeps skipping the last one; exit 10 on a required failure.
Why: the first thing a colleague runs when the tool does not work, and the prerequisite check the framework-
dependent publish (D-007) relies on.
Done when: every check has a pass, a warn and a fail case in the tests (with the module and the registry faked),
and the table names what to install for each failure.
