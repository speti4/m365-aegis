---
size: S
origin: wip/083-spike-snapshot-store-format-and-size.md
created: 2026-10-30
started: 2026-10-31
closed: 2026-10-31
plan: none
milestone: M5 Snapshot and views
---

# 092 — Spike: the sharing-link read for the snapshot, its consent and its time over the tenant

## Context

A measured spike of the one read the snapshot of task 087 lacks: the shared items and their grantees per site through
a drive's `delta` with `Prefer: hierarchicalsharing` and each marked item's `permissions`, the group behind a site,
whether OneDrives are in or out, and how long it takes over the tenant's sites, under `Sites.Read.All` only: the
maintainer declined `Sites.FullControl.All` on 2026-10-31 as too strong a consent for this tool, so the spike measures
what the read-only scope gives (which items and permissions it returns, what it misses) and names the gap; the answer
is a note in D-011 or a decision of its own, not code.
Why: D-011 assumes about 170,000 shared items at most and a read the Core has no route for yet; what the read-only
scope cannot reach narrows the store's sharing scope before task 087 builds it.
Done when: the route, the consent and the measured time over a few live sites are written down, and task 087's text
names the route it builds.
Tenth round of 2026-10-31, in the main session on `main` (a spike with no code to build, the one signed-in round of
the round): the measurements run with the broker's cached token through a throwaway script in the scratchpad, the
tenant's values stay in a scratch file, and only counts, timings and scrubbed shapes enter this file and the decision.

## Docs that move

- `tasks/decisions/D-011-snapshot-store-format.md`: the route and the scope's gap as a dated note, or a new decision
  if the scope narrows the store's sharing domain
- `docs/development.md`, "Snapshot": one line on the sharing read's scope (written by task 087 with the route)

## Not in this change

- The read's code in the Core and the take (task 087 builds the route this spike names)
- A consent change of the app registration (`Sites.FullControl.All` is declined)

## Steps

1. [x] The route on paper: the drive's `delta` with `Prefer: hierarchicalsharing`, the marked items' `permissions`, the
       group behind a site (`/sites/{id}/drive` and `/groups/{id}/sites/root`), what the Graph reference says about
       `Sites.Read.All` for each call
2. [x] The measurement over a few live sites with the cached token: items returned, items marked as shared, the
       permissions read per marked item and what came back empty or forbidden, the time per site, OneDrives in or out
3. [x] The extrapolation to the tenant's hundred-odd sites and the gap named: what the read-only scope misses and what the
       store's sharing domain holds under it
4. [x] D-011's note (the maintainer chose the note over a new decision on 2026-10-31), the route sent to the task
       session of 087, the Verification dated; `tasks_check` with `write` at the close on `main`

## Log

- 2026-10-31 — promoted on `main` by the main session (a spike with no build; the admin-bypass housekeeping commit)
- 2026-10-31 — measured with a throwaway .NET 10 console in the scratchpad (a project reference to the Core for
  `AuthService.Create().GetAccessTokenAsync(Resource.Graph)`, then `HttpClient` with the `Prefer` header, which the
  Core's `GraphRequest` cannot send; one cached token, no sign-in). The sample: the six drives of the tenant's hundred-odd (the
  four largest by quota used, 70 to 720 GB, and two from the middle), the `permissions` of up to 60 marked items a
  drive; the raw answers stayed in the scratchpad
- 2026-10-31 — the route sent to the task session of 087 (below, Verification); OneDrives are out under the consent;
  the maintainer chose a dated note in D-011 over a new decision (the shape holds, the sharing domain narrows), written
  the same day; the `docs/development.md` line is task 087's with the route (its *Docs that move*)
- 2026-10-31 — task 087 built the route in #122 and the live take read some twenty thousand sharing rows with 0 failed items once
  the permission batches honored `Retry-After` (its Verification); closed on `main` with the round

## Deviations from the plan

- `/groups/{id}/sites/root` is not the route to the group behind a site (1 of 5 Microsoft 365 groups answered):
  the drive's `owner.group` from `/sites/{id}/drive` is, and it comes in a `$batch` for every site at once
- The `shared` facet's `scope` on a delta item is not the link's scope (`users` on 358 of 372 marked items while the
  permissions carried 459 organization links): the facet marks the item, the `permissions` call tells what is shared

## Verification

- 2026-10-31 — the read under delegated `Sites.Read.All`, the maintainer's cached token, over the tenant:

  | call | result |
  | --- | --- |
  | `GET /sites?search=*` | a hundred-odd sites, one page, 1,165 ms; no OneDrive (personal site) among them |
  | `GET /sites/{id}/drive?$select=id,owner,quota,driveType`, 6 `$batch` of 20 | 4,404 ms; a hundred-odd drives, most owned by a group (`owner.group`), a few sites without a drive (404), 2 `accessDenied` (403) for the administrator |
  | `GET /drives/{id}/root/delta?$select=…,shared,…&$top=1000` + `Prefer: hierarchicalsharing`, six drives | some thirty thousand items in 74,056 ms (about 2 s a 1000-item page); about 4% marked `shared`; the largest drive over ten thousand items, 14 pages, 27,320 ms, a few hundred marked |
  | the same first page without the header | all but one of the first page's items marked: the preference is honored although no `Preference-Applied` header is returned |
  | `GET /drives/{id}/items/{item}/permissions`, 274 marked items | 274 × 200, 96,945 ms (354 ms a call), about two thousand entries (7.5 an item): a quarter of them links (nearly all organization links, almost all `edit`; a few dozen `users` links; anonymous 0), three quarters direct grants (site groups half, users a third, Entra groups the rest, a couple of siteUsers), a tenth inherited; roles owner, write and read about 3:3:1; the grantees of a `users` link in `grantedToIdentitiesV2[].user`, an organization link with an empty list (a few dozen) |
  | `GET /users/{id}/drive`, 3 users | 403 for a provisioned OneDrive, 404 for two unprovisioned; `GET /me/drive` 200 |
  | `GET /groups/{id}/sites/root`, 5 Microsoft 365 groups | 1 × 200, 4 refused: not the route |

- 2026-10-31 — the extrapolation: the four largest drives carry a few hundred marked items each, the two middle ones a
  few dozen; a few thousand marked items over the drives, some twenty thousand grantee rows once the site groups and the
  inherited entries are dropped (D-011's model: some ten thousand items and a little over ten thousand grantees; stress
  170,000 items). Sequential, the
  delta is about 2 ms an item and the permissions 0.35 s a marked item (about 26 minutes for the tenant); `$batch` of
  20 and a few drives in parallel bring the sharing domain within minutes for a nightly take. Nothing was throttled.
- 2026-10-31 — the gap under the read-only consent: other users' OneDrives (403) stay out of the store, and a site the
  administrator cannot open (2 of the hundred-odd) is recorded as failed in the manifest; the shape of D-011 holds, the sharing
  domain is the SharePoint sites' document libraries

## Closing checklist

- [x] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
      (none of its own; the incremental read from the delta links is task 099, filed from task 087's pull request)
- [x] every doc under *Docs that move* changed in this change (D-011's note; the docs/development.md line came with
      task 087's "Snapshots" section in #122)
- [x] Verification written, dated
- [x] `tasks_check` green, README index regenerated
- [x] moved to `done/` with `closed: 2026-10-31`
