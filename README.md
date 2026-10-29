# m365-aegis

A thin Microsoft 365 operations layer for Windows 11: one sign-in for Graph and Exchange Online, a command line
whose answers a shell, a script or an agent can parse, and a window with guided task cards for the support team.
Groups, Teams and SharePoint; Exchange Online mailboxes; audit, sign-ins and reports. Every write behind a plan
and a confirmation.

Planned output (M1):

```text
> m365-aegis group owners-missing
name                     id                                    members
-----------------------  ------------------------------------  -------
Finance                  00000000-0000-0000-0000-000000000031  14
Project Phoenix          00000000-0000-0000-0000-000000000032  6

> m365-aegis mailbox access finance@contoso.com --json
{"ok":true,"data":[{"user":"alice@contoso.com","rights":["FullAccess"]}],"meta":{"requestIds":[],"elapsedMs":412,"backend":"pwsh"}}
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
says what is missing.

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

When something does not work, `doctor` checks the prerequisites (pwsh and the Exchange Online module, the sidecar
script, WebView2, the device's join state, the sign-in, the endpoints) and says what to install or do for each
warning and failure. A sign-in that fails is diagnosed with `--verbose login`: the sign-in library's log, without
tokens or personal data, goes to `%LOCALAPPDATA%\m365-aegis\logs\`.

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
