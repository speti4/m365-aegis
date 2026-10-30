---
status: proposed
date: 2026-10-30
cost: medium
sources: [wip/083-spike-snapshot-store-format-and-size.md]
---

# D-011 — The snapshot store's format, place, query path and retention

## Context

M5's views answer "who can reach what" from a snapshot instead of live reads: live, the reverse mailbox lookup alone
takes 41 s over 620 mailboxes (task 043), while the access view of task 084 has to answer in under a second. Task 087
builds the store and `snapshot take`; this decision gives it the shape. The constraints: the data lives under the
home folder and never in the repo (D-005), a snapshot holds no token or secret, and its source is the Core's existing
reads plus the one read it lacks (sharing).

**What a snapshot holds.** The Core already reads the groups with their kind, mail and sync flag (`GroupInfo`), the
direct and transitive members with the via group (`GroupMember`, tasks 033 and 041), the owners in Graph and, for the
distribution and mail-enabled security groups, `ManagedBy` (task 076), the teams (`TeamInfo`) and the sites
(`SiteInfo`), every mailbox (`MailboxInfo`), FullAccess, Send As and Send on Behalf in both directions
(`MailboxHolder`, `UserGrant`), the forwarding (`Forwarder`) and a mailbox's calendar permissions
(`FolderPermissionEntry`). It has no read of sharing links or direct grants on files, and no read of the group behind
a site: task 087 adds both. Graph's documented way to find shared items at scale is the drive's `delta` with
`Prefer: hierarchicalsharing` and the `permissions` of each item it marks, and the delta reference says that
processing permissions correctly needs `Sites.FullControl.All`
(https://learn.microsoft.com/graph/api/driveitem-delta?view=graph-rest-1.0#scanning-permissions-hierarchies), a
consent the app registration does not have yet.

**The counts.** The fixtures are too small to scale by ratio (7 users, 9 groups, 6 mailboxes, 7 mailbox grants, 3
forwarders, 5 folder permission entries over 2 calendars, 3 sites, 2 teams, no sharing link), so the spike's model
takes the tenant counts already measured, 621 mailboxes (task 048), 2445 groups (task 032) and 38 forwarders (task
055), and estimates the rest: 850 users (520 with a mailbox, 100 shared mailbox accounts, 150 guests, 80 without a
mailbox), 2445 groups (280 Microsoft 365, 1100 security, 900 distribution, 165 mail-enabled security), 51,600 direct
member edges (most groups 1 to 12 members, 14% 20 to 120, 1% all-staff; 10% hold one to three groups), 60,100 user to
group rows of the transitive closure, 1,660 owners, 400 sites (280 behind a group), 220 teams, 9,900 shared items with
12,400 grantees (25 a site), 1,240 mailbox grants, 2,290 calendar permission entries and 38 forwarders: 143,000 rows.
A stress model doubles the groups and puts 250 shared items on a site: 616,000 rows, 170,000 shared items. The live
counts are the maintainer's check before deciding (below).

**The measurements** (task 083, 2026-10-30, the maintainer's machine, a warm file cache; the write and the in-process
query in Python 3.13 with its standard library; the cold process is a .NET 10 console over the same files, its
whole wall time with the process start: System.Text.Json into typed records, or Microsoft.Data.Sqlite 10.0.14
read-only). The question is task 084's: the groups alice is in (direct and nested), the sites behind them, the shared
items granted to her or her groups, the organization-wide links, her mailbox grants and calendar permissions.

| shape | size | write | query, in process | query, cold .NET process |
| --- | --- | --- | --- | --- |
| JSON, one file per domain | 20.2 MB | 68 ms | 65 ms | 250 ms |
| JSON, one file per domain, gzip | 3.3 MB | 432 ms | 91 ms | 255 ms |
| JSON, one document | 20.2 MB | 73 ms | 72 ms | |
| JSON, one document, gzip | 3.3 MB | 438 ms | 100 ms | |
| JSON Lines, one file per table | 20.3 MB | 263 ms | 168 ms | |
| SQLite, one file, text ids as keys | 24.7 MB (5.8 MB gzip) | 306 ms | 4.4 ms | 111 ms |
| SQLite, one file, integer keys | 5.1 MB | 114 ms | 1.9 ms | 110 ms |

The stress model: JSON per domain 112.6 MB, write 396 ms, cold .NET 880 ms; its gzip 20.2 MB, 2.6 s, 915 ms; JSON
Lines 113.2 MB, query 993 ms in process; SQLite with text keys 118.4 MB, 3.2 s, 124 ms; SQLite with integer keys
30.5 MB, 650 ms, 119 ms. A JSON shape pays for the whole snapshot on every question (the load is 140 ms of the 250 ms
and grows with the rows); SQLite pays for the answer (the open is 14 ms, the query 16 ms here and 22 ms under stress).
The nested groups as a recursive query over the direct edges answer in 5.1 ms against 4.4 ms for the stored closure.

## Decision

- **The shape: one SQLite file per snapshot**, integer keys. An `objects` table holds every user, group and mailbox
  once (the integer key, the object id as text, the kind, the user principal name, the name, the mail) with unique
  indexes on the object id and the user principal name; the edges refer to it by integer: `members` (direct),
  `userGroups` (the transitive closure, computed at take time from the direct edges, with the via group as
  `group members --via` names it), `owners` (with `graph` or `managedBy` as the source), `sites` (with the group
  behind it), `teams`, `links` and `linkGrantees` (a shared item's scope, roles, expiry and path, and who holds it),
  `grants` (mailbox, right, holder), `folderPermissions` and `forwarding`, each edge table indexed on its principal.
  A `manifest` table and `pragma user_version` carry the schema version, the time taken, the profile, the tenant, the
  tool's version, which read answered each domain and what failed (a partial snapshot says so and stays usable).
- **The place:** `<home>\snapshots\<profile>\<yyyy-MM-ddTHHmmssZ>.sqlite`, the UTC time of the take, one folder per
  profile so two tenants never mix. The take writes `<name>.sqlite.partial` with the journal off and renames it when
  complete, so a reader never opens half a snapshot; a `.partial` left by a failed run is removed by the next take.
  The folder inherits the home folder's access list (task 075) and the doctor's `home-access` check covers it.
- **What a snapshot never holds:** a token, anything of the MSAL cache, a secret or a credential; a sharing link's URL
  (an anonymous link's URL is the access itself) or password; a file's, a message's or a mailbox's content; a
  sign-in or audit record; any field the reads do not return. Task 087 extends the token hygiene test of task 072
  over the snapshots folder.
- **The query path for `access <user>`:** the newest complete snapshot of the profile, opened read-only; the user by
  user principal name, mail or object id in `objects`; the groups from `userGroups`; the sites whose group is among
  them or owned by the user; the shared items from `linkGrantees` whose principal is the user or one of the groups;
  the organization-wide and anonymous links and the calendars' `Default` entries as counts, since they reach
  everyone; the mailbox grants and calendar permissions whose holder is the user or one of the groups. One statement
  per section; the envelope names the snapshot's time and warns when it is older than 36 hours.
- **The retention:** every snapshot of the last 2 days, the newest of each day for 14 days and the newest of each ISO
  week for 8 weeks, about 22 files (110 MB at the estimated size, 670 MB under stress); `keepDaily` and `keepWeekly`
  per profile in `config.json` override the defaults. A take prunes after it succeeds, never after a failure (a
  failing schedule never empties the folder), never the newest snapshot and never a file whose name is not a
  snapshot's; the envelope lists what it pruned. This is not a plan (D-006): a snapshot is a copy of reads that the
  administrator's retention governs, not a write to the tenant and not an audit record like a plan record (task 066).
- **The schedule:** a Task Scheduler task the documentation gives (`Register-ScheduledTask` with `-LogonType
  Interactive` and `-StartWhenAvailable`, daily at a morning hour), running `m365-aegis snapshot take --profile
  <name>` as the administrator, only while that user is logged on: the MSAL cache is protected with the user's DPAPI
  key and the broker answers in the user's session, so a task that runs without the user logged on cannot sign in. The
  take uses the silent sign-in only (D-003, as every lookup does) and never device code; a refused sign-in is exit
  code 3 in the log, and the views keep answering from the last snapshot with its age.
- **The package:** `Microsoft.Data.Sqlite` (MIT), which brings `SQLitePCLRaw.bundle_e_sqlite3`, `.core`,
  `.lib.e_sqlite3` and `.provider.e_sqlite3` (Apache-2.0, on `licenses/allowed.json`) and about 2.2 MB in the release
  folder, 2.0 MB of it the native `e_sqlite3.dll` (win-x64), which D-007's framework-dependent folder carries on disk.
  Task 087 adds it with its reason in `Directory.Packages.props`.
- Cost: medium, a package with a native library and a schema to version. Proposed by the spike of task 083 on
  2026-10-30; the maintainer decides.

**The maintainer's check before deciding** (one signed-in round): the tenant's users and guests, its groups by kind,
the direct member edges (the sum of the groups' member counts), the sites and how many are behind a group, and the
shared items of a few sites (a drive's `delta` with `Prefer: hierarchicalsharing`), whether the tenant consents to
`Sites.FullControl.All` for that read, the mailbox grants (`mailbox access --user` totals) and the calendars' entries.
The decision holds if the shared items stay within the stress model (170,000); the JSON shapes stay viable only well
under it.

## Not chosen

- **JSON, one file per domain, gzip** (3.3 MB, 255 ms cold): no package and the smallest file, the runner-up; but the
  load grows with the snapshot (915 ms cold under stress, over the budget with the process start), every view would
  rebuild its own indexes in memory, and a comparison of two snapshots means loading both.
- **JSON, one file per domain, plain** (20.2 MB, 250 ms cold; 880 ms under stress): the same load, six times the disk.
- **One JSON document, plain or gzip** (20.2 MB or 3.3 MB, 72 or 100 ms in process): the per-domain files' numbers
  with no way to read one domain alone.
- **JSON Lines, one file per table** (20.3 MB, 168 ms in process, 993 ms under stress): appendable while the take
  runs, but the slowest to read and 14 files a snapshot.
- **SQLite with text ids as keys** (24.7 MB, 111 ms cold; 118.4 MB under stress): the same query time as integer keys
  at five times the size and a 3.2 s write under stress.
- **One SQLite file for every snapshot** (a snapshot id column): one file to open and prune, but a prune becomes a
  delete inside a large file that needs a `VACUUM`, and one damaged file loses every snapshot; a comparison of two
  snapshots attaches the second file instead.
- **The transitive closure as a recursive query** (5.1 ms against 4.4 ms): saves the 60,000 closure rows but loses
  the via group that task 041 shows, which the take computes once anyway.
- **An app-only certificate profile for an unattended schedule** (the M5 roadmap's Teams export profile): it would
  run without the user logged on, but the Exchange reads need `Exchange.ManageAsApp` and a role for the app, a
  separate consent and certificate; a later choice once that profile exists.
