# m365-aegis — set up your tenant

This guide is for the administrator who brings m365-aegis into a Microsoft 365 tenant: you register the tool once,
consent to the permissions it uses, sign in once to check it, and hand the team three values. The people who then use
the tool follow the [usage guide](usage.md).

m365-aegis has no shared application: every tenant registers its own public client, and the tool signs in as the
person using it, with that person's own roles. Nothing of yours leaves your tenant: there is no server, no secret and
no certificate in the registration, and the tool keeps its sign-in cache, profiles and logs on each user's machine
under `%LOCALAPPDATA%\m365-aegis\`.

The examples use made-up names (`contoso.onmicrosoft.com`, `alice@contoso.com`); put your own in their place.
`<application id>` stands for the Application (client) ID your registration gets.

## Before you start

- **The machine.** Windows 11 with the runtimes the [usage guide](usage.md#what-the-machine-needs) lists, and the
  release unzipped as its [Check the download](usage.md#check-the-download) section says.
- **Who registers the app.** An Application Administrator or a Cloud Application Administrator, or any user if your
  tenant still lets users register applications.
- **Who consents.** Granting tenant-wide admin consent for delegated permissions takes a Cloud Application
  Administrator, an Application Administrator or a Privileged Role Administrator (a Global Administrator can do it
  too). Every permission m365-aegis asks for is delegated; it asks for no application permission.
- **Who uses it.** Each person who signs in needs, for each command they run, the role in the last column of the
  [permissions table](#permissions). A delegated permission never goes beyond the account's own rights: consenting to
  `Group.ReadWrite.All` lets the tool change only the groups the signed-in account may change anyway.

## Register the app

The registration is a single-tenant public client with two redirect URIs on the "Mobile and desktop applications"
platform, public client flows allowed, and no credential.

### In the Microsoft Entra admin center

1. Open the Microsoft Entra admin center (https://entra.microsoft.com), go to **App registrations** and choose
   **New registration**.
2. **Name**: `m365-aegis` (any name works; it is what the consent screens and the sign-in logs show).
   **Supported account types**: "Accounts in this organizational directory only" (single tenant). Leave the redirect
   URI empty and choose **Register**.
3. On the **Overview** page, note the **Application (client) ID** and the **Directory (tenant) ID**.
4. **Authentication** > **Add a platform** > **Mobile and desktop applications**. Add two custom redirect URIs:
   - `ms-appx-web://microsoft.aad.brokerplugin/<application id>`, the Windows account manager's (the broker's)
     redirect, with your Application (client) ID in place of `<application id>`;
   - `http://localhost`, for the browser sign-in when the broker cannot be used.
5. Still under **Authentication**, set **Allow public client flows** to **Yes** (the device-code sign-in needs it) and
   save.
6. Add no certificate and no client secret: m365-aegis is a public client and never authenticates as the app.
7. **API permissions** > **Add a permission** > **Microsoft Graph** > **Delegated permissions**: add the Microsoft
   Graph permissions of the [table](#permissions) for the commands your team will use.
8. **Add a permission** again > the **APIs my organization uses** tab > search for **Office 365 Exchange Online** >
   **Delegated permissions** > **Exchange**: add `Exchange.Manage` and `Exchange.ManageV2`.
9. Choose **Grant admin consent for** your organization and confirm. Every row of the list then says "Granted".

### With the Azure CLI

The same registration with the Azure CLI (`az`, signed in with `az login --tenant contoso.onmicrosoft.com
--allow-no-subscriptions` as an account that may register apps and consent). The permission ids are looked up by name
from the Microsoft Graph and Office 365 Exchange Online service principals of your tenant; edit the two name lists to
leave out what your team will not use:

```powershell
$appId = az ad app create --display-name m365-aegis --sign-in-audience AzureADMyOrg `
    --is-fallback-public-client true --query appId -o tsv
az ad app update --id $appId --public-client-redirect-uris "ms-appx-web://microsoft.aad.brokerplugin/$appId" "http://localhost"
az ad sp create --id $appId

$graph = "00000003-0000-0000-c000-000000000000"     # Microsoft Graph, the same app id in every tenant
$exchange = "00000002-0000-0ff1-ce00-000000000000"  # Office 365 Exchange Online, the same app id in every tenant
function Add-DelegatedPermission([string] $api, [string[]] $names) {
    $scopes = az ad sp show --id $api --query oauth2PermissionScopes -o json | ConvertFrom-Json
    $ids = foreach ($name in $names) { ($scopes | Where-Object value -eq $name).id + "=Scope" }
    az ad app permission add --id $appId --api $api --api-permissions $ids
}
Add-DelegatedPermission $graph User.Read, User.ReadBasic.All, GroupMember.Read.All, Team.ReadBasic.All, Sites.Read.All,
    AuditLog.Read.All, Application.Read.All, ExchangeMessageTrace.Read.All, Reports.Read.All,
    GroupMember.ReadWrite.All, Group.ReadWrite.All
Add-DelegatedPermission $exchange Exchange.Manage, Exchange.ManageV2
az ad app permission admin-consent --id $appId
$appId                                              # the application id to hand to the team
```

`--is-fallback-public-client true` is "Allow public client flows"; `--public-client-redirect-uris` sets the "Mobile and
desktop applications" platform's redirect URIs. Each `az ad app permission add` ends with a warning that a
`permission grant` is needed to make the change effective: the `admin-consent` line is that grant, for every
permission at once, so nothing else is needed. The parameters are those of the Azure CLI reference for
[`az ad app`](https://learn.microsoft.com/cli/azure/ad/app) and
[`az ad app permission`](https://learn.microsoft.com/cli/azure/ad/app/permission); the block was run end to end
against a throwaway registration with Azure CLI 2.71 on 2026-11-01.

## Permissions

The one list of what m365-aegis asks for. "Microsoft Graph" permissions are under **Microsoft Graph** in the portal;
"Exchange Online" ones under **APIs my organization uses** > **Office 365 Exchange Online**. The tool asks each API
for a token with every permission you consented to (the `.default` scope), so a permission you leave out costs only
the commands that need it: they end with exit code 8 (`forbidden`), and `whoami` shows which permissions each token
carries.

### Read-only: the lookups

| command group | delegated permission | API | role the account needs |
| --- | --- | --- | --- |
| `login`, `whoami`, the window's sign-in | `User.Read` | Microsoft Graph | none |
| `group members`, `group owners`, `group owners-missing` | `GroupMember.Read.All`, and `User.ReadBasic.All` for the names of members and owners and for the window's user picker; `Exchange.Manage` for a distribution group's owners (its "managed by") and `--all-types` | Microsoft Graph; Exchange Online for the distribution groups | none for Graph; the Exchange Online read role of the mailbox row for the distribution groups |
| `team list` | `Team.ReadBasic.All` | Microsoft Graph | none |
| `site list` | `Sites.Read.All` | Microsoft Graph | none; the list holds the sites the account can open |
| `signin list`, `audit list` | `AuditLog.Read.All` | Microsoft Graph | Reports Reader, Security Reader or Global Reader; the sign-in log also needs a Microsoft Entra ID P1 or P2 license in the tenant |
| `secrets expiring` | `Application.Read.All` | Microsoft Graph | none |
| `trace`, `trace details` | `ExchangeMessageTrace.Read.All`, and the [message trace service principal](#the-message-trace-service-principal) once per tenant | Microsoft Graph | an Exchange Online role group that carries the Message Tracking role: Recipient Management or Organization Management (also Records Management and Compliance Management; View-Only Organization Management does not carry it), per the [Exchange Online permissions reference](https://learn.microsoft.com/exchange/permissions-exo/permissions-exo#role-based-permissions); without one the service answers a 500 (exit code 9), not a refusal |
| `mailbox list`, `mailbox access`, `mailbox forwarders`, `mailbox folder-permissions`, `mailbox group-members`, `mailbox organization`, `mailbox size <mailbox>`, the window's mailbox cards | `Exchange.Manage` (the PowerShell sidecar) and `Exchange.ManageV2` (the Exchange Online Admin API); `GroupMember.Read.All` (Microsoft Graph) for `--expand-groups` | Exchange Online | View-Only Organization Management or Recipient Management (Exchange Administrator and Global Administrator include them) |
| `mailbox size` over every mailbox | `Reports.Read.All` (Microsoft 365's mailbox usage report) | Microsoft Graph | Reports Reader, or another role that reads the usage reports |
| `call` | what the endpoint's Microsoft Graph reference page names | Microsoft Graph | what that page names |

### Writes: every change behind a plan

| command group | delegated permission | API | role the account needs |
| --- | --- | --- | --- |
| `group members add`, `group members remove` (Microsoft 365 and security groups) | `GroupMember.ReadWrite.All`, `User.ReadBasic.All` | Microsoft Graph | Groups Administrator or User Administrator, or the group's owner |
| `group owners add`, `group owners remove` (Microsoft 365 and security groups) | `Group.ReadWrite.All`, `User.ReadBasic.All` | Microsoft Graph | Groups Administrator or User Administrator, or the group's owner |
| the same four writes on a distribution group or a mail-enabled security group | `Exchange.Manage`, `Exchange.ManageV2` | Exchange Online | Organization Management, or Recipient Management with Security Group Creation and Membership |
| `mailbox access grant`, `mailbox access revoke`, `mailbox forwarding set`, `mailbox forwarding clear`, the window's Mailbox rights and Mailbox forwarding cards | `Exchange.Manage`, `Exchange.ManageV2` | Exchange Online | Recipient Management or Organization Management |
| `call` with a method other than GET | what the endpoint's reference page names | Microsoft Graph | what that page names |

`plan list`, `plan show` and `plan prune` read and remove the local plan records and need no permission.

### Snapshots

| command group | delegated permission | API | role the account needs |
| --- | --- | --- | --- |
| `snapshot take` | `User.ReadBasic.All`, `GroupMember.Read.All`, `Team.ReadBasic.All`, `Sites.Read.All` | Microsoft Graph | none for the Graph parts; the shared files are read from the sites the account can open |
| `snapshot take` (the mailboxes, their rights, calendars and forwarding, the distribution lists' owners) | `Exchange.Manage`, `Exchange.ManageV2` | Exchange Online | View-Only Organization Management or Recipient Management |
| `snapshot list`, `snapshot show`, `access` | none: they read the snapshot on the machine | | |

A part of a snapshot the account cannot read is recorded as failed and the rest is kept. The other users' OneDrives
are never read: that would need a permission m365-aegis does not ask for.

## The message trace service principal

`trace` reads Microsoft Graph's message trace, which needs a service principal of Microsoft's message trace
application in your tenant, created once. Its application id is `8bd644d1-64a1-4d4b-ae52-2e0cbf64e373`, the same in
every tenant. With Microsoft Graph PowerShell:

```powershell
Connect-MgGraph -Scopes Application.ReadWrite.All
New-MgServicePrincipal -AppId 8bd644d1-64a1-4d4b-ae52-2e0cbf64e373
```

(`az ad sp create --id 8bd644d1-64a1-4d4b-ae52-2e0cbf64e373` does the same.) Provisioning can take several hours;
until it completes, `trace` ends with exit code 8 and names this step. Microsoft's
[message trace onboarding guide](https://learn.microsoft.com/exchange/monitoring/trace-an-email-message/graph-api-message-trace)
describes it.

## Sign in once and check it

On a machine with the release, sign in with the three values: the tenant, the application id and your admin account.
`login` saves them as the first tenant profile and opens the Windows sign-in window:

```powershell
m365-aegis login --tenant contoso.onmicrosoft.com --client-id <application id> --account alice@contoso.com
m365-aegis whoami
m365-aegis doctor
m365-aegis group members Finance
```

`whoami` lists the account and one token for Microsoft Graph and one for Exchange Online, each with its expiry and the
permissions it carries. A permission consented after the sign-in reaches the tool with the next `login`. `doctor`
checks the machine, the sign-in and the endpoints, and one lookup proves the whole path.

**Check the consent with the doctor's `consent` row.** You need not compare the tokens with the table by hand: the row
reads the permissions each token carries and weighs them against the [permissions table](#permissions). It passes when
every permission of the table is consented. When one is missing it warns, names each table row that lacks a
permission with the permissions it lacks, and its `fix` line under the table names what to consent to:

```text
consent  warn  not consented: signin list, audit list (AuditLog.Read.All)
fix consent: an administrator consents to AuditLog.Read.All (Microsoft Graph) in the portal (the setup guide's Permissions table), then `m365-aegis login`
```

A row you left out on purpose (a command your team will not use) can stay a warning. For one you want, add the
permission as in step 7 or 8 of [the registration](#in-the-microsoft-entra-admin-center) (or the `Add-DelegatedPermission`
lines of [the Azure CLI block](#with-the-azure-cli)), grant the admin consent again (step 9, or `az ad app permission
admin-consent`), then `m365-aegis login` and `m365-aegis doctor` once more. A row that says a resource has no token, so
its permissions are not read, points at the `sign-in` row above it, which says why.

**The tenant value.** Give the tenant as its `.onmicrosoft.com` name (`contoso.onmicrosoft.com`), the form Exchange
Online connects with as it is. The Directory (tenant) ID or another verified domain works too: the tool then asks
Microsoft Graph for the tenant's initial `.onmicrosoft.com` name before it connects to Exchange Online, one request
more (with `User.Read`).

## Hand it to the team

Each person who uses m365-aegis gets:

- the release zip from the [releases page](https://github.com/speti4/m365-aegis/releases), or the link to it;
- the tenant (`contoso.onmicrosoft.com`) and the application id;
- the admin account they sign in with (their own; the tool never shares one sign-in between people);
- the [usage guide](usage.md), which takes them from the download through `login`, `whoami` and `doctor` to the daily
  work.

### The signature, SmartScreen and Smart App Control

The exes and the tool's own assemblies in the release are signed with a self-signed certificate that the maintainer's
own team trusts. That certificate is not published and is not meant to be trusted elsewhere, so on your machines:

- **The doctor's `signature` row warns** that the certificate is not trusted on this machine. That is expected and
  harmless: the signature still proves that each file is unchanged and names its signer. The row fails only when a
  signature does not hold, which means a file changed after it was signed: delete the folder and unzip the release
  again.
- **SmartScreen** may stop the first start of a downloaded exe ("Windows protected your PC"), since the signer has no
  reputation with Microsoft. `Unblock-File` on the zip before you extract it, as the usage guide says, avoids the
  prompt; so does **More info** > **Run anyway** once per exe.
- **Smart App Control**, where it is on, runs only programs it can vouch for, and it blocks the m365-aegis exes. It has
  no exception per program: on such a machine the tool does not start.

Your trust path is the download itself, not the certificate: the checksum in `SHA256SUMS.txt` and the build
attestation (`gh attestation verify … --repo speti4/m365-aegis`), which proves that this repository's release workflow
built the zip from a tagged commit. The usage guide's [Check the download](usage.md#check-the-download) has the lines.

## Troubleshooting

Every failure ends with an `error:` line, a `hint:` line with the fix, and an exit code that always means the same
(the full list is in the usage guide's [Exit codes](usage.md#exit-codes)). The two you meet while setting up:

- **Exit code 3 (`auth`): no usable sign-in.** Run `m365-aegis login`. If `login` itself fails, check the
  registration: the broker's redirect URI with the right application id, `http://localhost`, and "Allow public client
  flows" (the device code sign-in needs it). Then run `m365-aegis --verbose login`: the sign-in library's log, without
  tokens or personal data, goes to `%LOCALAPPDATA%\m365-aegis\logs\`, one file a day, and names the step that failed
  (a Conditional Access policy, a consent, the account).
- **Exit code 8 (`forbidden`): the service refused the account or the app.** A permission is not consented, or the
  account lacks the role in the [table](#permissions). `whoami` shows which permissions each token carries; after a
  new consent, `login` once. `trace` answers 8 for hours after the message trace service principal was created, and
  `signin list` answers 8 in a tenant without a Microsoft Entra ID P1 or P2 license; both say so in the hint.
- **`trace` answers exit code 9 (`service-error`) with "Failed parsing error response".** The message trace service
  answers this way for an account without a role group that carries the Message Tracking role (the [table](#permissions);
  seen on 2026-11-01), instead of a refusal; the hint says so. Check the role before trying again later.

`m365-aegis doctor` checks the rest (the runtimes, PowerShell and the Exchange Online module, the endpoints through
the proxy and the firewall); the usage guide's [The doctor](usage.md#the-doctor) explains each row. When you ask for
help, attach the log file, never `config.json` or the cache, and describe the problem with made-up names
([SECURITY.md](../SECURITY.md) for a security problem).
