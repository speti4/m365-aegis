---
status: idea
size: S
origin: wip/089-group-owners-missing-judges-distribution-groups-by-managedby.md
created: 2026-10-31
milestone: M3 Writes with safety
---

# `group owners-missing --all-types` falls back to Graph's list when the account has no Exchange role

What: when the sidecar's `Get-DistributionGroup` is refused for lack of an Exchange role, `--all-types` answers with
Graph's list as it did before task 089, the backend stays `graph`, and a line under the table (a warning in the
envelope) says the distribution groups are judged by Graph's empty owner list because Exchange could not be asked.
Why: since task 089 the command fails outright without an Exchange role, where it used to give the Graph view; a
partial answer with a stated limit serves a reader better than an error, as `group owners` already does.
Done when: over the fake module with a refused Exchange sign-in the command exits 0 with Graph's list and the warning,
with the role it answers as task 089 made it, and the usage guide names the fallback.
