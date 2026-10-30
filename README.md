# m365-aegis

A thin Microsoft 365 operations layer for Windows 11: one sign-in for Graph and Exchange Online, a command line
whose answers a shell, a script or an agent can parse, and a window with guided task cards for the support team.
Groups, Teams and SharePoint; Exchange Online mailboxes; audit, sign-ins and reports. Every write behind a plan
and a confirmation.

What it prints (M1, against a made-up tenant):

```text
> m365-aegis group owners-missing
name             type           address              id
---------------  -------------  -------------------  ------------------------------------
Helpdesk Admins  security       -                    00000000-0000-0000-0000-000000000034
Project Phoenix  Microsoft 365  phoenix@contoso.com  00000000-0000-0000-0000-000000000033

2 group(s) without an owner
3 distribution, mail-enabled security or synced group(s) left out: Graph keeps no owners for them (--all-types lists them)

> m365-aegis group owners Finance --json
{"ok":true,"data":{"group":{"id":"00000000-0000-0000-0000-000000000031","displayName":"Finance","kind":"microsoft365","mail":"finance@contoso.com","synced":false},"owners":[{"type":"user","displayName":"Alice Example","userPrincipalName":"alice@contoso.com","mail":"alice@contoso.com","id":"00000000-0000-0000-0000-000000000001"}]},"meta":{"requestIds":["00000000-0000-0000-0000-0000000000e1","00000000-0000-0000-0000-0000000000e5"],"elapsedMs":412,"backend":"graph"}}
```

## What it will do

m365-aegis is under construction; the plan and its state are in [`tasks/`](tasks/README.md), the milestones in
[`tasks/roadmap.md`](tasks/roadmap.md). The first milestone is the sign-in, the Graph client and the first
lookups:

- **One sign-in.** The Windows account manager signs you in with the account you already use (silent on an
  Entra-joined machine), and the same sign-in serves Graph and the Exchange Online module.
- **Graph without the import tax.** A direct client: paging, throttling and batching handled once, v1.0 by
  default and `--beta` when needed, plus a raw `call` for any endpoint.
- **Exchange Online included.** Mailbox permissions, forwarding, shared mailboxes and sizes through a long-lived
  PowerShell sidecar running `ExchangeOnlineManagement` with a cmdlet allow-list; message trace through Graph.
- **Two outputs, one contract.** A table at a terminal, a JSON envelope in a pipe or for an agent; exit codes
  that mean one thing each. `--dry-run` shows a plan, `--yes` applies it.
- **A window for the team.** Task cards for the daily Exchange and group work the admin centers make slow, each
  ending in a confirmation.

## Requirements

Windows 11 with the .NET 10 runtime: the command line (`m365-aegis.exe`) needs the .NET Runtime 10, the window
(`m365-aegis-gui.exe`) the .NET Desktop Runtime 10, which includes the former (`winget install
Microsoft.DotNet.DesktopRuntime.10`). The Exchange Online commands need PowerShell 7.6 and the
`ExchangeOnlineManagement` module 3.10; the window's WebView2 runtime comes with Windows 11. `m365-aegis doctor`
says what is missing, the Desktop Runtime of the window included (its `dotnet-desktop-runtime` row).

## Build from source

m365-aegis is a .NET 10 core with a `System.CommandLine` command line and a WinForms window hosting a React front
end in WebView2; why is in [D-002](tasks/decisions/D-002-dotnet-10-core-cli-and-window-with-react.md) and D-009.
You need the .NET 10 SDK, Node 24 and PowerShell 7.6 (details in [CONTRIBUTING.md](CONTRIBUTING.md)):

```powershell
dotnet build -c Release
src\M365Aegis.Cli\bin\Release\net10.0\m365-aegis.exe doctor --offline
```

## First run

The tool signs in as a public client of your own app registration in your tenant; how to register it, and
which delegated permissions to consent to, is in
[docs/development.md](docs/development.md#the-sign-in-and-the-home-folder). Once, with the registration's
values, which `login` saves as the tenant profile under `%LOCALAPPDATA%\m365-aegis\config.json`; afterwards
`whoami` and every lookup sign in silently through the Windows account manager:

```powershell
$aegis = "src\M365Aegis.Cli\bin\Release\net10.0\m365-aegis.exe"
& $aegis login --tenant contoso.onmicrosoft.com --client-id <application id> --account alice@contoso.com
& $aegis whoami
```

Without a window (an SSH session, a scheduled task) `login` falls back to a device code on its own;
`login --device-code` asks for it. `logout` forgets the account and keeps the profile. A second tenant or account
is another `login --profile <name> …`; `profile list` shows the profiles, `profile use <name>` picks the default
and `profile remove <name>` signs one out and deletes it.

When something does not work, `doctor` checks the prerequisites (the .NET Desktop Runtime of the window, pwsh and
the Exchange Online module, the sidecar script, WebView2, the device's join state, the sign-in, the endpoints) and says what to install or do for each
warning and failure. A sign-in that fails is diagnosed with `--verbose login`: the sign-in library's log, without
tokens or personal data, goes to `%LOCALAPPDATA%\m365-aegis\logs\`.

Any Graph request goes through `call`, its answer unchanged inside the envelope; a method other than GET is a
write and is sent only with `--yes` (without it the request is printed as the plan):

```powershell
& $aegis call GET /me                    # v1.0; /beta/me or --beta for the beta endpoint
& $aegis call GET "/groups?`$top=5" --all # every page
```

The first lookups, each a table at a terminal and the envelope with `--json`; a group is named by its display
name, its address, its object id or a word of its name:

```powershell
& $aegis group members Finance
& $aegis group members Finance --transitive   # the nested groups' members too, each marked direct or nested
& $aegis group owners finance@contoso.com
& $aegis group owners-missing            # --all-types adds the groups Graph keeps no owners for
& $aegis team list
& $aegis site list --search finance
& $aegis trace --sender alice@contoso.com --days 3              # at most 10 days per trace
& $aegis trace --recipient bob@contoso.com --until 2026-10-01   # up to 90 days back
& $aegis trace --sender alice@contoso.com --status failed       # or --subject "Quarterly report" (contains)
& $aegis trace details "<message-id@contoso.com>"               # where a message stopped: the steps per recipient
& $aegis trace details <trace id> --recipient bob@contoso.com   # the trace id of a trace row, one recipient
```

Each needs its delegated permission consented for the app registration (docs/development.md, "The sign-in and
the home folder"); the message trace also needs a service principal created once per tenant.

The mailbox lookups run the Exchange Online module in a PowerShell sidecar the command starts and ends, signed in
with the same account (`Exchange.Manage`, and an Exchange role that reads recipients):

```powershell
& $aegis mailbox list --shared
& $aegis mailbox access finance@contoso.com            # who holds FullAccess, Send As, Send on Behalf
& $aegis mailbox access --user alice@contoso.com       # every mailbox alice can open or send as
& $aegis mailbox forwarders                            # forwarding mailboxes, the external targets first
& $aegis mailbox size --top 10                         # sizes from the usage report, the largest first
```

The audit lookups read the logs of the last `--days N` (1 by default, 30 at most) or from `--since <date>`, the
newest first:

```powershell
& $aegis signin list --user alice@contoso.com --days 7  # the sign-ins of a user (needs Microsoft Entra ID P1)
& $aegis signin list --failures                         # the failed sign-ins of every user in the last day
& $aegis signin list --kind service-principal --failures # also non-interactive, managed-identity (beta endpoint)
& $aegis secrets expiring 30                            # app secrets and certificates that expire within a month
& $aegis audit list --days 7 --activity "Add member"    # the directory audit, by the start of the activity's name
& $aegis audit list --user alice@contoso.com --days 3   # what one admin changed
& $aegis audit list --since 2026-10-28 --until 2026-10-28T15:27:00Z  # continue a list cut at --limit
```

A list cut at `--limit` (1000 by default) names the oldest entry it reached and the `--since … --until …` that
lists the ones before it.

The window, `src\M365Aegis.Gui\bin\Release\net10.0-windows\m365-aegis-gui.exe`, works with the same tenant profile
and sign-in as the command line. Its first page shows the signed-in account, read silently as `whoami` does, and
a group's members in a table, as `group members` lists them; a lookup that fails is a notification with the hint.
`m365-aegis-gui --verbose` writes the sign-in library's log to the same `logs\` folder as the command line's
`--verbose`.

## Mailbox reads through the Admin API and group expansion

Some mailbox reads go through the Exchange Online Admin API, a REST call with the same sign-in and no PowerShell
start (`Exchange.ManageV2` for the app registration); a group that holds a grant, or nests other groups, is read
through Graph:

```powershell
& $aegis mailbox access finance@contoso.com --expand-groups     # a group holder's members, every nesting level
& $aegis mailbox access --user alice@contoso.com --expand-groups # also the mailboxes alice reaches through a group
& $aegis mailbox folder-permissions finance@contoso.com         # the calendar's, in any language; --folder Inbox for another
& $aegis mailbox group-members sales@contoso.com                # a distribution group's members, hidden ones too
& $aegis mailbox organization                                   # MailTips settings and the accepted domains
& $aegis group members Finance --via                            # the nested group each nested member comes through
& $aegis mailbox size finance@contoso.com                       # one mailbox's live size
```

A folder given with `--folder` is named as the mailbox's own language names it. `mailbox access --user` reads every
mailbox's permissions with 4 sidecars at once (about 40 s over 620 mailboxes; `--sidecars N`, or `--method piped`
for one). `mailbox size` over every mailbox reads Microsoft 365's mailbox usage report, a few days old, whose day
is under the table (`Reports.Read.All` for the app registration); `--method piped` or `parallel` reads the live
sizes instead.

## The window: sign-in and the mailbox page

The window signs in from its sign-in card once the command line has written the tenant profile (`login --tenant …
--client-id … --account …`, above): "Sign in" opens the Windows account picker over the window, "Sign in with a
code" shows a device code and the page to enter it on, and the card shows the account as soon as the sign-in
completes. "Sign out" forgets the account as `logout` does and keeps the profile.

The mailbox access card asks the same questions as `mailbox access`: who holds FullAccess, Send As and Send on
Behalf on a mailbox, which mailboxes a user holds them on, and which shared mailboxes there are to pick from. The
window keeps one Exchange Online sidecar for as long as it is open: the first lookup starts it (a few seconds),
every later one answers in the cmdlet's own time, and the sidecar ends with the window or with "Sign out".

## Writes behind a plan

Every change to the tenant is a plan first: the command reads what is there, lists only what is not so already
(the state now and after), and sends nothing until you say so. `--dry-run` prints the plan; without a flag the plan
is printed and the exit code is 7; `--yes` applies it, keeps going past a change the service refuses (exit code 6,
each change's result in the answer) and records the plan with every result under
`%LOCALAPPDATA%\m365-aegis\plans\`. A plan warns before you confirm: a group's last owner, a grant to a group, an
address outside the organization.

```powershell
& $aegis group members add Finance dave@contoso.com bob@contoso.com --dry-run   # bob is a member already
& $aegis group owners remove Finance alice@contoso.com --yes
& $aegis mailbox access grant finance@contoso.com --user bob@contoso.com --right FullAccess,SendAs --dry-run
& $aegis mailbox access revoke finance@contoso.com --user alice@contoso.com --right SendOnBehalf --yes
& $aegis mailbox forwarding set bob@contoso.com --to bob@fabrikam.com --keep-copy --dry-run
& $aegis mailbox forwarding clear bob@contoso.com --yes
```

A list of changes is one plan from a CSV file, a header line and one change per line (`group,user` for the group
writes, `mailbox,user,right` for the mailbox rights, `mailbox,to,keepCopy` and `mailbox` for the forwarding); a
file with a malformed line is refused whole before anything is sent:

```powershell
& $aegis mailbox access grant --from-csv grants.csv --dry-run
& $aegis group members add --from-csv new-team.csv --yes
```

The group writes need `GroupMember.ReadWrite.All` (and `Group.ReadWrite.All` for the owners) consented for the app
registration; the mailbox writes an Exchange Online role that changes recipients. The columns, the exit codes and the
plan record are in [docs/development.md](docs/development.md#writes-and-plans).

## Documentation

- [Development](docs/development.md): layout, CI, releases and helper scripts
- [Changelog](CHANGELOG.md): every release and the tasks it closed, with the zips and checksums on the
  [releases page](https://github.com/speti4/m365-aegis/releases) once there is a release
- [Contributing](CONTRIBUTING.md) and the [security policy](SECURITY.md)
- [Why m365-aegis exists](docs/origin.md) and [the tooling landscape it was measured against](docs/research/)
- [Decisions](tasks/decisions/): build or not, the stack, the sign-in, Exchange Online, the private repo, the
  output contract, the publish

## Project status

Pre-alpha: nothing is released or downloadable yet, and the commands answer with the task that will build
them; build from source as above. The plan lives in [`tasks/`](tasks/README.md): the backlog, the work in
flight and the history, one file per item. Windows 11 is the platform.

m365-aegis is for a Microsoft 365 administrator who wants the daily lookups and changes of one tenant to take a
second instead of a module import, and to hand some of them to the support team with a safety net.

## License

[MIT](LICENSE). Microsoft 365, Microsoft Graph, Exchange Online and Entra are Microsoft's; m365-aegis is an
independent tool and ships none of their code or branding.
