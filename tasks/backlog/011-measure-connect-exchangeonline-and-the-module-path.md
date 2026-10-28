---
status: ready
size: S
origin: session 2026-10-28
created: 2026-10-28
milestone: M1 Foundation
---

# Measure: cold `Connect-ExchangeOnline` with and without `-CommandName`, and the module path under OneDrive

What: on the maintainer's machine, the cold connect time of the Exchange Online module in three variants
(default; `-SkipLoadingFormatData`; both with `-CommandName` limited to the sidecar's allow-list), five runs
each, median and spread; and whether `$env:PSModulePath` starts under a OneDrive-synced Documents folder, which
the research names as a module discovery slowdown. The numbers go into `docs/origin.md` next to the Graph
import measurements of 2026-10-21.
Why: the sidecar's warm-up budget and the doctor's advice depend on them; nothing published gives a figure.
Done when: the numbers are in `docs/origin.md` with the date and the module version, and the sidecar's connect
uses the fastest variant that still answers the allow-list.
