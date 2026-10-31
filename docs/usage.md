# m365-aegis — the usage guide

m365-aegis is a small Microsoft 365 operations tool for an administrator and the support team they hand tasks to:
one sign-in serves Microsoft Graph and Exchange Online, the lookups answer the daily questions (who is in a group, who
can open a mailbox, where a message went, who signed in), and every change goes through a plan you read before
anything is sent. It comes as one zip with two programs in one folder:

- `m365-aegis.exe`, the command line, for a PowerShell window or a script;
- `m365-aegis-gui.exe`, the window, with cards for the most common tasks.

This is the user's guide: for anyone who has the zip and the three values of a tenant where m365-aegis is set up,
with no copy of the source needed. It is shipped in the zip as `GUIDE.md`. Setting the tool up in a tenant (the app
registration, the permissions and the consent) is the administrator's part, once per tenant, in the
[setup guide](https://github.com/speti4/m365-aegis/blob/main/docs/setup.md).

Every example below uses made-up names (`contoso.com`, `alice@contoso.com`, the group Finance, the shared mailboxes
finance and reception); put your own in their place. The commands are written as `m365-aegis …`: run them from the
unzipped folder as `.\m365-aegis.exe …`, or put the folder on your `PATH`.

## Before the first start

### What the machine needs

The zip carries the tool, not the runtimes. Install these once (an administrator may have done it for you already):

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10   # the .NET 10 runtime, with the Desktop Runtime the window needs
winget install Microsoft.PowerShell                 # PowerShell 7.6 or later, for Exchange Online
winget install Microsoft.EdgeWebView2Runtime        # the window's web view; Windows 11 usually has it
pwsh -Command "Install-Module ExchangeOnlineManagement -Scope CurrentUser"
```

Then run the doctor from the unzipped folder; it checks all of the above and says what to do about anything missing
([The doctor](#the-doctor) explains each row):

```powershell
m365-aegis doctor --offline
```

### Check the download

A release is a zip, `m365-aegis-<version>.zip`, with `SHA256SUMS.txt` and its release notes next to it on the
releases page (https://github.com/speti4/m365-aegis/releases). Before you unzip it:

1. The checksum: `(Get-FileHash .\m365-aegis-<version>.zip).Hash` prints the value of `SHA256SUMS.txt` in capitals.
2. The build: with the GitHub command line installed, `gh attestation verify .\m365-aegis-<version>.zip --repo
   speti4/m365-aegis` shows that the repository's release workflow built the zip.
3. Once both match, `Unblock-File .\m365-aegis-<version>.zip` removes the mark Windows puts on a download, so the
   exes do not each ask on their first start; then extract the zip into a folder of your own, such as
   `%LOCALAPPDATA%\Programs\m365-aegis`.

The checksum and the attestation are what tell you the download is the release: check both on every machine.

The exes and the tool's own assemblies are also signed, with a self-signed certificate that the maintainer's own team
trusts on its machines; the certificate is not published, so on any other machine the signature reads as not trusted.
In the unzipped folder, `Get-AuthenticodeSignature .\m365-aegis.exe` names the signer, which the release notes name
too, and `m365-aegis doctor` checks every signed file in its `signature` row:

- The row passes only on a machine that trusts the release's certificate, whose thumbprint the tool knows and the
  release notes print.
- It warns that the certificate is not trusted on any other machine. That is expected: the signature still shows that
  each file is unchanged since it was signed, and the checksum and the attestation above stand for the rest.
- It warns that the signer is not the release's certificate when another certificate signed the files: they did not
  come from the release, so delete the folder and unzip the release again.
- It fails when a signature does not hold: the file changed after it was signed. Delete the folder and unzip the
  release again.

SmartScreen may stop the first start of an exe that still carries the download mark ("Windows protected your PC"):
`Unblock-File` above avoids it, or **More info** > **Run anyway** once. Where Smart App Control is on, Windows blocks
the exes and has no exception per program, so the tool does not start on such a machine.

## Sign in

The administrator who set the tool up in your tenant gives you three values: the tenant (`contoso.onmicrosoft.com`
or its id), the application id of the tool's app registration, and your admin account. The first sign-in writes them into a tenant profile:

```powershell
m365-aegis login --tenant contoso.onmicrosoft.com --client-id <application id> --account alice@contoso.com
m365-aegis whoami
```

`login` opens the Windows sign-in window over the console; choose the account named in the profile. Without a
window (an SSH session, a scheduled task) it falls back to a device code: it prints a page and a code on the
console, you open the page in a browser and enter the code. `m365-aegis login --device-code` asks for the code
directly. After the first time, `m365-aegis login` alone signs in again with the profile.

`whoami` shows the account, the profile and each token: one for Microsoft Graph, one for Exchange Online, with
their expiry. A token that is missing with a consent error means the administrator has not consented to a
permission yet. Every lookup signs in silently and fails with exit code 3 and `login` as the fix when it cannot.
`m365-aegis logout` signs the account out and keeps the profile.

### Profiles

A second tenant or a second account is a second profile:

```powershell
m365-aegis login --profile lab --tenant fabrikam.onmicrosoft.com --client-id <application id> --account alice@fabrikam.com
m365-aegis profile list
m365-aegis profile use work
m365-aegis profile remove lab
```

`profile list` marks the default with `*`; the first profile is the default, `profile use` moves it, and
`--profile <name>` on any command uses another one for that command. `profile remove` signs the profile's account
out and deletes the profile.

When the sign-in fails in a way you cannot explain, `m365-aegis --verbose login` writes the sign-in library's log
into the logs folder (no tokens, no personal data); attach that file when you ask for help.

## How the answers look

At a terminal every command prints a table; in a pipe or a script it prints one line of JSON, the envelope
(`{"ok":…,"data":…,"error":…,"meta":…}`). `--table` and `--json` force one or the other:

```powershell
m365-aegis group members Finance --table
m365-aegis group members Finance --json | ConvertFrom-Json
```

A failure prints `error: <message> (<code>)` and a `hint:` line with the fix, and ends with an exit code that
always means the same ([Exit codes](#exit-codes)). `m365-aegis --help`, and `--help` after any command, lists the
commands and options; `m365-aegis --version` prints the version.

## Lookups

Lookups only read: nothing in the tenant changes. A group, a user or a mailbox can be named by its display name, its
address or its object id; a word of a group's name works too, and when it matches several groups the answer (exit
code 4) lists them, so you can name the one you meant.

### Groups

```powershell
m365-aegis group members Finance                  # the direct members
m365-aegis group members Finance --transitive     # every member of the nested groups too, each marked direct or nested
m365-aegis group members Finance --via            # with the nested group each nested member comes through
m365-aegis group owners Finance
m365-aegis group owners-missing                   # every group without an owner
m365-aegis group owners-missing --all-types       # also distribution, mail-enabled security and synced groups
```

The owners of a distribution group or a mail-enabled security group are read from Exchange Online (their
"managed by" list), since Microsoft Graph does not keep them. `owners-missing --all-types` judges these groups the
same way: one is listed only when its "managed by" list in Exchange Online is empty, and the line under the table
counts the ones left out because it names an owner. A group synced from on-premises Active Directory is listed as
Microsoft Graph sees it, with a line under the table that says so. Without `--all-types` the command asks Microsoft
Graph alone, as before.

### Teams and SharePoint

```powershell
m365-aegis team list                              # every team of the tenant
m365-aegis site list                              # the SharePoint sites your account can see
m365-aegis site list --search phoenix             # the sites whose name or address has the word
```

### Exchange Online mailboxes

The mailbox commands start an Exchange Online session through PowerShell in the background, which takes a few
seconds the first time.

```powershell
m365-aegis mailbox list --shared                  # the shared mailboxes (without --shared, every mailbox)
m365-aegis mailbox access finance                 # who holds FullAccess, Send As and Send on Behalf on it
m365-aegis mailbox access reception --expand-groups   # a right held by a group, expanded into its members
m365-aegis mailbox access --user bob@contoso.com  # the other way round: the mailboxes Bob can open or send as
m365-aegis mailbox forwarders                     # the mailboxes that forward their mail, the external targets first
m365-aegis mailbox size --top 3                   # the three largest mailboxes
m365-aegis mailbox folder-permissions reception   # who holds which right on the calendar
m365-aegis mailbox group-members sales            # a distribution group's members as Exchange keeps them, hidden ones too
m365-aegis mailbox organization                   # the MailTips settings and the accepted domains
```

`mailbox access --user` reads every mailbox of the tenant, which takes a while in a large tenant; add
`--expand-groups` to include the mailboxes the user reaches through a group. `mailbox size` without a mailbox reads
Microsoft's usage report, which is a few days old; `mailbox size alice` reads one mailbox live, and
`--method parallel` reads every mailbox live. `mailbox folder-permissions` takes `--folder` for another folder
than the calendar (`--folder Inbox/Reports` for a nested one, in the mailbox's own language).

### Mail flow

```powershell
m365-aegis trace --sender alice@contoso.com --until 2026-10-20T12:00:00Z --status failed
m365-aegis trace details 00000000-0000-0000-0000-000000000051
```

`trace` lists the messages a sender sent (`--sender`) or a recipient received (`--recipient`) over the last two days,
or `--days` up to 10, ending now or at `--until` (at most 90 days back); `--status` and `--subject` narrow it.
`trace details` takes the trace id of a row, or the message's Message-ID in quotes (`"<message-id@contoso.com>"`),
and lists the delivery steps of that message for each recipient; `--recipient` keeps one of them. The message trace
needs a one-time setup by the administrator; until it is done, `trace` answers with exit code 8 and names the step.

### Sign-ins, audit and app credentials

```powershell
m365-aegis signin list --user alice@contoso.com --since 2026-10-28T12:00:00Z --failures
m365-aegis audit list --since 2026-10-28T12:00:00Z --activity "Add member"
m365-aegis secrets expiring 30                    # app secrets and certificates that expire within 30 days
```

`signin list` reads the Microsoft Entra sign-in log, newest first: by default the last day of every user's
interactive sign-ins; `--days` (up to 30) or `--since` and `--until` set the window, `--kind` picks the
non-interactive, service principal or managed identity sign-ins, and `--limit` caps the count. The log needs a
Microsoft Entra ID P1 or P2 license; without one the command says so with exit code 8. `audit list` reads the
directory audit log the same way, `--activity` by the start of the activity's name and `--user` by who did it. A
list that stops at `--limit` (1000 by default) names the oldest entry it reached, and the line under the table gives
the `--since … --until …` that lists the ones before it. `secrets expiring` lists the secrets and certificates of the app registrations and service principals, the soonest
first; `--include-expired` adds the ones that have already expired.

### Any Microsoft Graph request

`call` sends a Graph request of your own and prints the answer unchanged; GET is a read, any other method is a
change and goes through a plan like the writes below:

```powershell
m365-aegis call GET /me
m365-aegis call GET /me --beta                    # the beta endpoint
m365-aegis call PATCH /groups/00000000-0000-0000-0000-000000000031 --body '{"description":"Finance team"}' --dry-run
```

`--all` follows the pages of a long list, and `--advanced` sends the header an advanced directory query needs.

## Changes: a plan first, then `--yes`

Every change is a plan first. The command reads the current state, compares it with what you asked for, and lists
only what is not so already, each change from what to what. Nothing is sent until you apply the plan:

- `--dry-run` prints the plan and sends nothing (exit code 0);
- no flag prints the plan, sends nothing, and ends with exit code 7, so a script cannot change anything by mistake;
- `--yes` applies the plan, one change after another, and prints each change's result.

```powershell
m365-aegis group members add Finance dave@contoso.com --dry-run
m365-aegis group members add Finance dave@contoso.com --yes
```

When nothing needs to change, the plan says it is up to date and nothing is sent. An apply keeps going past a
change the service refused; it ends with exit code 6 and lists which changes were made, which failed (with the
service's message) and which were skipped.

### The writes

```powershell
m365-aegis group members add Finance dave@contoso.com --dry-run
m365-aegis group members remove Finance carol@contoso.com --dry-run
m365-aegis group owners add Finance bob@contoso.com --dry-run
m365-aegis group owners remove Finance alice@contoso.com --dry-run
m365-aegis mailbox access grant finance --user bob@contoso.com --right FullAccess --dry-run
m365-aegis mailbox access grant finance --user bob@contoso.com --right FullAccess --no-automapping --yes
m365-aegis mailbox access revoke finance --user alice@contoso.com --right FullAccess --dry-run
m365-aegis mailbox forwarding set alice --to alice@fabrikam.com --keep-copy --dry-run
m365-aegis mailbox forwarding clear bob --dry-run
```

`group members add` and `remove` take several users at once. `--user` and `--right` repeat on the mailbox rights
(`--right` takes `FullAccess`, `SendAs` and `SendOnBehalf`, also comma-separated); `--no-automapping` grants
FullAccess without Outlook adding the mailbox for the user on its own. `mailbox forwarding set` forwards to the
address only, unless `--keep-copy` keeps a copy in the mailbox; the plan warns when the address is outside the
organization. The members and owners of a distribution group or a mail-enabled security group, which Microsoft Graph
cannot change, are changed through Exchange Online, with the same plan, flags and exit codes. A synced group's members
and owners belong to the on-premises directory: the plan warns that the service will refuse the change.

### A batch from a CSV file

Every write above also takes `--from-csv <file>` instead of the arguments: a header line naming the columns, then one
change per line, all of it one plan.

| write | columns |
| --- | --- |
| `group members add`, `group members remove`, `group owners add`, `group owners remove` | `group`, `user` |
| `mailbox access grant`, `mailbox access revoke` | `mailbox`, `user`, `right`; a grant optionally `automapping` (`false` on a FullAccess line) |
| `mailbox forwarding set` | `mailbox`, `to`, optionally `keepCopy` (`true` or `false`) |
| `mailbox forwarding clear` | `mailbox` |

```powershell
m365-aegis group members add --from-csv group-members-add.csv --dry-run
m365-aegis mailbox access grant --from-csv mailbox-grants.csv --dry-run
```

A file saved by Excel with semicolons works too. The whole file is checked before anything is read from the tenant:
a missing column or a broken line refuses it with exit code 2 and the line's number.

### The plan records

Every apply is recorded under the `plans` folder of the home folder ([Where the data lives](#where-the-data-lives)):
the plan, who applied it, from the command line or the window, and each change's result.

```powershell
m365-aegis plan list                              # the records, newest first: when, the write, applied and failed
m365-aegis plan show 20261029T100000.000Z-mailbox-access-grant
m365-aegis plan prune --older-than 90 --dry-run   # the records older than 90 days, which --yes removes
```

`plan show` takes a name as `plan list` prints it. A record is the audit trail of a change and the source of a
resume, so `plan prune` is a write itself: a plan first, then `--yes`.

### Resume an apply that did not finish

When an apply ended partly done (exit code 6) or stopped (the sign-in expired, the session broke), `--resume`
continues it from its record: the changes it left failed or skipped are planned again against the current state,
and one made in the meantime comes out as already done.

```powershell
m365-aegis group members add --resume 20261030T110000.000Z-group-members-add --dry-run
```

The resume is the same write as the record (the hint names it if you pick another one), with no other arguments,
and `--dry-run` or `--yes` as usual. A change the window applied resumes the same way.

## Snapshots

A snapshot is a copy of who can reach what in the tenant, read in one run and kept as one file under
`%LOCALAPPDATA%\m365-aegis\snapshots\<profile>\`: the users and groups with their members (nested ones too) and
owners, the teams, the SharePoint sites with the group behind each one and their shared files, and the Exchange Online
mailboxes with FullAccess, Send As, Send on Behalf, the calendars' permissions and the forwarding. Reading all of it
takes minutes in a large tenant; a question to a snapshot takes well under a second. Taking one only reads: nothing in
the tenant changes.

```powershell
m365-aegis snapshot take                          # read the tenant into a new snapshot, then remove the old ones
m365-aegis snapshot list                          # the snapshots kept, newest first
m365-aegis snapshot show                          # the newest one: when, what each part was read from, what failed
m365-aegis snapshot show 2026-10-31T060000Z       # one by its name as the list prints it
```

A part that could not be read (a permission the account lacks, a site it may not open) is named by `snapshot take`
and `snapshot show`, and the rest of the snapshot is kept and used; when a whole part failed, the take ends with
exit code 6 and keeps the older snapshots. After a take that read every part, the old snapshots are removed: every
snapshot of the last two days is kept, then the newest of each day for 14 days and the newest of each week for 8
weeks. `keepDaily` and `keepWeekly` in the profile of `config.json` change the 14 and the 8. The other users'
OneDrives are not read: that needs a permission the tool does not ask for.

### What one user reaches

```powershell
m365-aegis access dave@contoso.com                # groups, sites, shared files, mailboxes and calendars of one user
m365-aegis access dave@contoso.com --json | ConvertFrom-Json
```

`access` answers from the newest snapshot, without signing in: the groups the user is in (and through which group a
nested membership comes), the sites of those groups and of the groups the user owns, the files shared with the user
or one of the groups, the mailbox rights and the calendar permissions held directly or through a group. The links
anyone in the organization or anyone at all can open, and the calendars open to every user, reach this user like
everyone else, so they are counted under the table rather than listed. The answer names the snapshot's time and warns
when it is older than 36 hours. Without a snapshot it ends with exit code 4 and the hint
`run m365-aegis snapshot take first`.

### A snapshot every morning

A Task Scheduler task takes the snapshot while you are logged on; the sign-in it uses is the one of your Windows
session, so it runs only while you are logged on and never asks for a password:

```powershell
$action = New-ScheduledTaskAction -Execute "C:\Tools\m365-aegis\m365-aegis.exe" -Argument "snapshot take --profile work --json"
$trigger = New-ScheduledTaskTrigger -Daily -At 7:00
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable
Register-ScheduledTask -TaskName "m365-aegis snapshot" -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

`-StartWhenAvailable` runs a take that was missed (the machine was off at 7:00) at the next logon. When the sign-in is
no longer valid, the take ends with exit code 3 (the task's last result) and takes nothing; `m365-aegis login` once
fixes it, and `access` keeps answering from the last snapshot with its age.

## The window

`m365-aegis-gui.exe` opens the window. It uses the same tenant profile, the same sign-in and the same plans as the
command line; the first profile is written by `m365-aegis login` on the command line. Each task is a card.

### Sign-in

The card shows the account, the profile, the tenant and each token's expiry, read silently as `m365-aegis whoami`
does. Signed out, "Sign in" opens the Windows sign-in window over this one, and "Sign in with a code" shows a device
code instead, with "Open in browser" for its page and "Copy the code"; the card updates when the sign-in completes.
"Sign out" signs the account out; "Refresh" reads the state again. Without a profile the card says so: run
`m365-aegis login` on the command line once.

### Group members

A group's name, address or object id, then "Look up": the group's type and its direct members in a table, as
`m365-aegis group members` lists them.

### Mailbox access

"A mailbox" or "A user", then the address and "Look up": who holds FullAccess, Send As and Send on Behalf on the
mailbox, or the mailboxes the user holds them on, as `m365-aegis mailbox access` reads them. "List the shared
mailboxes" lists them as buttons; pick one to see its access. A right held by a group is listed as the group. The
first lookup starts the Exchange Online session, which takes a few seconds. When the Mailbox rights card grants or
revokes a right on the mailbox shown here, the card reads the mailbox again on its own and says "Read again after a
change on the Mailbox rights card." over the table.

### Mailbox rights

Grants or revokes FullAccess, Send As or Send on Behalf on a mailbox, as `m365-aegis mailbox access grant` and
`revoke` do. The steps are the same on every write card: Pick, Plan, Confirm, Result.

1. Pick: "Grant" or "Revoke", the mailbox, the users (several, separated by commas) and the rights; for a FullAccess
   grant, "Without automapping" keeps Outlook from adding the mailbox on its own. "See the plan".
2. Plan: the window computes the plan, the same one `--dry-run` prints, and nothing is changed yet: what changes, what
   is already as asked, the warnings ("Good to know"). "Change the picks" goes back.
3. Confirm: the changes once more; tick "I checked the plan; make these changes." and press "Confirm and apply",
   which stands for `--yes`. "Back to the plan" changes nothing.
4. Result: "Done", "Partly done" or "Not done", each change with its result and the service's message. The apply is
   recorded like one from the command line, so `m365-aegis plan list` shows it and `--resume` continues a partial
   one. "Done" starts the card over.

"Refused: nothing was changed" means the window did not send anything (a plan older than 15 minutes, for one: see
the plan again). "Stopped: check the mailbox before you try again" means the apply broke off and some changes may
have been made: look at the mailbox, or at `m365-aegis plan list`, before you try again. A card that needs a
sign-in says so and waits for the sign-in card.

### Mailbox forwarding

Forwards a mailbox's mail to an address, or stops it, as `m365-aegis mailbox forwarding set` and `clear` do:
"Forward" or "Stop forwarding", the mailbox, the address, and "Keep a copy in the mailbox" (ticked by default here;
on the command line a copy needs `--keep-copy`). Then the same steps as the mailbox rights card; without the copy, the
plan's "Good to know" says that the mail reaches the address only and that ticking the box keeps one.

The group writes, the CSV batches and a resume are on the command line only. `m365-aegis-gui.exe --verbose` writes
the window's sign-in log as `--verbose` does on the command line.

## The doctor

`m365-aegis doctor` is the first thing to run when something does not work. Each row is a check with `pass`,
`warn`, `fail` (`FAIL` when the tool cannot work without it) or `skip`, and every `warn` and `fail` has a `fix`
line under the table. A `FAIL` ends with exit code 10; anything else with 0. `--offline` skips the sign-in and the
three endpoints, the checks that need the network.

| check | `warn` means | `fail` means, and the fix |
| --- | --- | --- |
| `home` (required) | the home folder does not exist yet; the first sign-in creates it | it is a file, not a folder: move the file away |
| `home-access` | an account other than you, `SYSTEM` and `Administrators` can read your profiles, plan records and logs (the row names each one with its right): the fix line is an `icacls` command that gives the folder back to you alone | (never fails) |
| `dotnet-runtime` | (a fact: the .NET version the tool runs on) | |
| `dotnet-desktop-runtime` | it is a patch behind the .NET runtime: `winget upgrade Microsoft.DotNet.DesktopRuntime.10` | not installed, the window cannot start: `winget install Microsoft.DotNet.DesktopRuntime.10` |
| `pwsh` (required) | PowerShell's version could not be read | missing or older than 7.6: `winget install Microsoft.PowerShell` |
| `exchange-online-module` | older than 3.10: `Update-Module ExchangeOnlineManagement` | not installed, the mailbox commands cannot run: `Install-Module ExchangeOnlineManagement -Scope CurrentUser` |
| `sidecar-script` (required) | the Exchange Online script differs in its line endings only | the script is missing or was changed: unzip the release again |
| `signature` | a file is not signed (a build from source, or an unsigned zip), its certificate is not trusted on this machine (expected outside the maintainer's team: [Check the download](#check-the-download)), or a certificate that is not the release's signed it (unzip the release again) | a signature does not hold, the file changed after it was signed: unzip the release again |
| `webview2-runtime` | it is old, so Edge Update is blocked here | not installed, the window cannot start: `winget install Microsoft.EdgeWebView2Runtime` |
| `config` | no profile yet (`m365-aegis login`), or no default (`m365-aegis profile use <name>`) | `config.json` does not read, or a profile lacks a value |
| `device` | the machine is registered, not joined, or has no primary refresh token: the profile's account signs in instead of the Windows account, which is normal on a registered machine | neither joined nor registered: Settings > Accounts > Access work or school > Connect |
| `sign-in` | no profile, or a token is missing (the administrator consents to the permission) | the silent sign-in failed: `m365-aegis login` |
| `endpoint-login`, `endpoint-graph`, `endpoint-exchange` (required) | a proxy asks for authentication, or the answer took over 3 seconds | no answer within 10 seconds: the network, the proxy or the firewall blocks `login.microsoftonline.com`, `graph.microsoft.com` or `outlook.office365.com` |

```powershell
m365-aegis doctor                                 # every check, the sign-in and the endpoints too
m365-aegis doctor --offline --table
```

## Where the data lives

Everything the tool keeps is in `%LOCALAPPDATA%\m365-aegis\`, readable by your account alone:

- `config.json`: the tenant profiles;
- `msal-cache.bin`: the sign-in cache, encrypted for your Windows account and shared by the command line and the
  window;
- `plans\`: a record of every apply;
- `logs\`: one file a day, written only for an unexpected error or under `--verbose`, without tokens or personal
  data;
- `webview2\`: the window's browser data.

The unzipped folder holds no data, so a new release replaces it: delete the old folder, unzip the new one. The
`M365AEGIS_HOME` environment variable points the tool at another folder. Never attach `config.json`, the cache or a
plan record to a public bug report: they name your tenant and its users.

## Exit codes

| code | error code | meaning |
| --- | --- | --- |
| 0 | | done |
| 2 | `usage` | an unknown command or option, or a missing argument; nothing was called |
| 3 | `auth` | no usable sign-in: `m365-aegis login` |
| 4 | `not-found` | the named object or profile does not exist, or the name matches several |
| 5 | `throttled` | the service throttled the request and the retries ran out: try again later |
| 6 | `partial` | some changes or items failed; the answer lists both |
| 7 | `refused-without-yes` | a change without `--yes`; the plan is the answer |
| 8 | `forbidden` | the service refused the account or the app: a permission not consented, or a role missing |
| 9 | `service-error` | the service answered with another error, or could not be reached |
| 10 | `doctor-failed` | a required doctor check failed |
| 11 | `sidecar-unavailable` | the Exchange Online session could not start: PowerShell or the module is missing, or the connection was refused |
| 12 | `cmdlet-not-allowed` | an Exchange Online command outside the tool's allow-list was asked for; nothing ran |
| 13 | `cmdlet-failed` | an Exchange Online command failed |
| 70 | `internal` | the tool broke; the hint names the log file with the details |
