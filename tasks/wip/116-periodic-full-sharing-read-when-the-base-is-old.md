---
size: S
origin: wip/099-incremental-sharing-read-from-the-stored-delta-links.md
created: 2026-11-01
started: 2026-11-01
plan: none
milestone: M5 Snapshot and views
---

# 116 — A periodic full sharing read when the incremental base is older than a configured age

## Context

`snapshot take` reads the sharing domain whole on its own when the base snapshot of the incremental read (task 099) is
older than an age set in the profile (a week by default), and the manifest names the reason; `--full` keeps forcing it.
Why: the delta under `Sites.Read.All`, without `deltashowsharingchanges`, may miss a change that touched a permission
only; a nightly incremental run needs a net under it, and the administrator should not have to remember a weekly flag.
Done when: over the fixtures a base older than the age makes the take read every drive whole with the reason in the
manifest, a younger base stays incremental, and the usage guide states the rule and the setting.
Thirteenth round of 2026-11-01, worked in the main session on its own branch (no worktree, no task session).
Decided 2026-11-01 (plan session): the age is measured from the last whole read of the sharing domain, which the
manifest carries from take to take (`sharingWholeRead`), not from the base file's own age: in a chain of takes the
base is always the previous take, so its own age never reaches a week. The setting is `fullSharingDays` of the
profile in `config.json` (a whole number of days, default 7, 1 or more, as the retention). One timestamp for the
domain: when it is old, every drive is read whole and the timestamp resets. A base taken before this change has no
timestamp and is read whole once (`unknown`). The manifest names the reason of a whole read (`sharingWholeReason`:
`full`, `no-base`, `age`, `unknown`), and the domain's note says it for `age` and `unknown`.
Decided 2026-11-01 (the maintainer, D-014): the snapshot stays, in an on-demand frame; the tool runs ad hoc, a take is
a batch started before asking, and a schedule stays an option. The age rule is the net under an ad hoc chain, where an
incremental take may start from a base weeks old.

## Docs that move

- `docs/usage.md`: the snapshot intro says a take comes before the question; the incremental read's rule gains the
  age, the profile setting and what `snapshot show` says; the schedule section is framed as the option it is
- `docs/development.md`: the incremental paragraph gains the two manifest keys, the four reasons, the rule and the setting
- `tasks/decisions/D-014-snapshot-on-demand-not-nightly.md`: new, the on-demand frame (decided by the maintainer)
- `tasks/roadmap.md`: M5 says "taken on demand or on a schedule" instead of "nightly"
- `tasks/decisions/D-011-snapshot-store-format.md`: unchanged, it lists no manifest key (the store's format is the same)

## Not in this change

- The `deltashowsharingchanges` preference and its consent (a different permission; stays out, as task 099 left it)
- A periodic full read of any other domain (they are read whole every take today)
- The retention rule of D-011

## Steps

1. [x] `TenantProfile.FullSharingDays` and its `ProfileStore` entry, kept across a login as the retention is;
       `SnapshotTake.FullSharingDaysOf` validated in `RunAsync` before any network call
2. [x] `SharingBase.WholeRead` and the two manifest keys; the take's `Reads` knows the take's time; `SharingAsync`
       decides the base by reason, writes the keys, and the note says why for `age` and `unknown`; `--full` unchanged
3. [x] Core tests: the carried time in an incremental take, the flag's reason, a chain that reads whole at the age
       and goes incremental again, a base without the time; `dotnet format`, build, every test, the coverage gate green
4. [x] D-014, the roadmap's M5, `docs/usage.md`, `docs/development.md`; the Verification dated; `tasks_check` clean

## Log

- 2026-11-01 — promoted on `task/116-periodic-full-sharing-read-when-the-base-is-old` by the main session
- 2026-11-01 — the plan session's three questions (the age's base, the setting's name, the granularity) and the
  maintainer's challenge of the snapshot part answered as the Context records; D-014 written `decided`
- 2026-11-01 — `TenantProfile.FullSharingDays` (`fullSharingDays`, null → 7) read and written by `ProfileStore` as
  `keepDaily` is, a login keeping the hand-written value; `SnapshotTake.FullSharingDaysOf` (usage error below 1, the
  hint names the setting) runs right after `RetentionOf`. `SharingBase.WholeRead` reads `sharingWholeRead`; `Reads`
  takes the take's `started` and the days; `BaseOf()` returns the base to read from (null for a whole read), the
  snapshot loaded (for the note) and the reason: `full`, `no-base`, `unknown` (no time in the base), `age`
  (`(started - WholeRead).TotalDays >= days`), or null for an incremental read. The manifest gets `sharingWholeRead`
  (the take's own time after a whole read, else the base's) and `sharingWholeReason` when there is one; the note gains
  "every drive read whole: the last whole read is N day(s) old, and the profile reads whole every D day(s)" or "every
  drive read whole: <base> does not say when its drives were last read whole". A first take and a `--full` take keep a
  null note, so no output snapshot moved.
- 2026-11-01 — tests: `ASecondTakeReadsOnlyWhatTheDeltaLinksReportChanged` holds the carried time and no reason;
  `AFullTakeReadsEveryDriveWhole…` holds `full`; new `ATakeReadsEveryDriveWholeAgainWhenTheLastWholeReadIsOld`
  (`FullSharingDays = 2`: seed `no-base`, +1 day incremental with the seed's time, +2 days whole with `age`, three
  whole-read delta pages and no stored token, +3 days incremental from the whole read) and
  `ATakeReadsEveryDriveWholeWhenTheBaseDoesNotSayWhenItWasReadWhole` (the seed's key deleted → `unknown`); the
  retention edge tests gained `FullSharingDaysOf` and the hand-written `fullSharingDays` kept across three saves.
- 2026-11-01 — the docs: the usage guide's snapshot intro ("take one before you ask"), the age rule and the setting in
  "Only what changed in the libraries", the schedule section framed as the option; the development guide's incremental
  paragraph; the roadmap's M5; the take's budget comment says "a scheduled run". D-011 untouched (no manifest key list).

## Deviations from the plan

- The backlog item measured the age from the base snapshot's own age; the plan session changed it to the time of the
  last whole read, carried in the manifest, because in a chain of takes the base is always the previous take (the
  Context has the decision).

## Verification

- 2026-11-01 — `dotnet format --verify-no-changes` clean; `dotnet build -c Release -p:SkipFrontendBuild=true` 0
  errors; `dotnet test` 1632 passed, 0 failed; the coverage gate: `M365Aegis.Core.dll` lines 6692/6692 and branches
  3455/3455 (100%), `m365-aegis.dll` lines 2157/2157 and branches 548/548 (100%); `changelog.py --check` and
  `check-fixtures.py` (self-test and the files) clean. No live take in this change: the next signed-in round (tasks
  117 and 108) will show the first take after the upgrade reading whole with `unknown` and the next one incremental.

## Closing checklist

- [ ] every follow-up is a `backlog/` file (`/tasks add`, approved by the owner) or deleted with a reason in the Log
- [ ] every doc under *Docs that move* changed in this change
- [ ] Verification written, dated
- [ ] `tasks_check` green, README index regenerated
- [ ] moved to `done/` with `closed: YYYY-MM-DD`
