# The Microsoft 365 administration tooling landscape, and the stack question

Verified: 2026-10-28, in three research rounds (the market, the no-new-app and agent paths, the technology
stacks). Microsoft changes these products quickly; re-check a claim before relying on it. A statement marked
*unverified* was found in one source only or could not be checked against an official page.

## The question

Is a new tool for the day-to-day administration of a Microsoft 365 tenant worth building, for three domains:
groups, Teams and SharePoint (membership, owners, sites, lists, sharing, Teams chat export); Exchange Online
mailboxes (permissions, shared mailboxes, forwarding, size, retention, mail flow); and audit, sign-in and
reports (sign-in and audit logs, application secret expiry, usage reports, Conditional Access, Intune devices)?
For two users: a scripting administrator who also works from Claude Code sessions, and an IT support team that
works in the admin centers.

## The alternatives

Coverage: **GTS** groups, Teams and SharePoint · **EXO** Exchange mailboxes · **AUD** audit, sign-in, reports.
● strong · ◐ partial · ○ none.

| Tool | Kind | GTS | EXO | AUD | Auth | State on 2026-10-28 |
|---|---|---|---|---|---|---|
| Microsoft Graph PowerShell SDK | PowerShell module | ● | ◐ (mail data, message trace; no mailbox permissions) | ● | delegated (WAM) or app-only | v2.41.1 (2026-10-07); v3 planned for Q4 2026, PowerShell 7 only, a generator change with no import-time promise |
| Exchange Online PowerShell v3 (`ExchangeOnlineManagement`) | PowerShell module, REST cmdlets | ◐ | ● | ◐ (message trace, mailbox audit) | delegated or certificate | 3.10.1 (2026-07); 3.10.0 made PowerShell 7.6 the minimum |
| Exchange Online Admin API | REST, cmdlet-shaped POST | ○ | ◐ (6 endpoints) | ○ | OAuth, `Exchange.ManageV2` | public preview since 2025-11-17; "not a replacement for Exchange Online PowerShell" |
| The admin centers (M365, Entra, Teams, SharePoint, Exchange) | web | ● | ● | ● | browser | the click-ops baseline; bulk edits in the EAC |
| Microsoft 365 Admin agent | Copilot Chat agent | ◐ | ○ | ◐ | the admin's own roles, confirmation before a write | GA 2026-07-23, no extra license; users, licenses, groups, Teams troubleshooting, MFA, service health; **no Exchange scenario** |
| Microsoft MCP Server for Enterprise | remote MCP | ◐ (Entra) | ○ | ◐ | delegated | preview, read-only, Entra only, 3 tools |
| Microsoft Graph CLI (`mgc`) | CLI | — | — | — | — | **retired 2026-08-28**, no successor |
| CLI for Microsoft 365 (`m365`, PnP) | Node CLI + MCP server | ● (SharePoint strongest) | ○ (one `exo` command) | ◐ | delegated or certificate | v11.11 (2026-08-31), 1,471 stars |
| PnP.PowerShell | PowerShell module | ● | ○ | ◐ | own app registration required since 2024-09 | 3.4.1 (2026-08-19), PowerShell 7.4+ |
| merill/msgraph | Go CLI and agent skill | generic Graph | ○ | generic | MSAL | v1.0.19 (2026-03-26), 119 stars; a local index of 27,700 endpoints |
| Lokka (merill) | MCP server | generic Graph and Azure RM | ○ | generic | interactive, secret, certificate | 2.1.2 (2026-06-19), 310 stars; guardrails off by default |
| Softeria ms-365-mcp-server | MCP server | ◐ (user-centric) | ○ | ○ | delegated | 1,091 stars |
| ms-teams-cli | Rust CLI | ◐ (Teams) | ○ | ○ | delegated or app | 52 stars, no export command |
| Graph X-Ray | browser extension | helper | helper | helper | portal session | 1.1.10 (2026-05), AGPL-3.0; shows the Graph calls behind a portal action |
| Maester | PowerShell test framework | ○ | ○ | ● (configuration) | Graph | 1,164 stars |
| Script collections | PowerShell scripts | ● | ● | ● | Graph and EXO | admindroid-community 2,039 stars; Office365itpros 1,803 stars |
| AdminDroid | reporting GUI | ● | ● | ● | app consent | free edition plus paid |
| ManageEngine M365 Manager Plus | on-premises web app | ● | ● | ● | app | free up to 25 users |
| Syskit Point, CoreView, ShareGate, Quest On Demand | SaaS suites | ● | ◐–● | ◐–● | app | enterprise pricing (Syskit: a 100-user minimum) |
| Hornetsecurity 365 Permission Manager, EasyLife 365 | SaaS, one domain | ● | ○ | ○ | app | permissions, or lifecycle governance |

No MCP server wraps Exchange Online cmdlets (a GitHub search found none). The free GUIs are thin; the one
general-purpose open-source admin GUI was archived in 2025-01.

## What only Exchange Online PowerShell does

Graph has grown into Exchange territory, but the administrative core stays in the module. In the module only:

- creating a shared, room or equipment mailbox and converting a mailbox's type;
- FullAccess (`*-MailboxPermission`) and SendAs (`*-RecipientPermission`); Send on Behalf and folder permissions
  are also in the Admin API preview;
- mailbox-level forwarding (`ForwardingSmtpAddress`, `DeliverToMailboxAndForward`), remote-domain and
  outbound-spam auto-forward controls (Graph has per-user inbox rules only);
- creating or changing distribution groups and mail-enabled security groups (read-only in Graph); dynamic
  distribution groups are not in Graph at all;
- retention and holds: MRM policies and tags, litigation hold, archive, recoverable items;
- mail flow: transport rules, connectors, quarantine, anti-spam and anti-phishing policies;
- live mailbox and folder sizes (`Get-EXOMailboxStatistics`; the Graph usage report lags by about a day);
- calendar processing, hiding from the address list, mobile devices, mailbox audit configuration.

In Graph v1.0 now: message trace (`/admin/exchange/tracing/messageTraces`, `ExchangeMessageTrace.Read.All`, a
10-day window, 90 days back, 100 requests per 5 minutes; a service principal is provisioned once), the mailbox
usage report, `mailboxSettings` and inbox rules, mailbox import and export. The Exchange Online Admin API v2.0
(preview) is a POST-only cmdlet-shaped surface at `outlook.office365.com/adminapi/v2.0/{tenant}/{endpoint}` with
six endpoints (AcceptedDomain, DistributionGroupMember, DynamicDistributionGroupMember, Mailbox read and
Send-on-Behalf, MailboxFolderPermission, OrganizationConfig), the `X-AnchorMailbox` header required, `nextLink`
pages that expire in 5 to 10 minutes, and a `Get-Mailbox` output that shrinks to core properties at GA. The
`/adminapi/beta/InvokeCommand` endpoint the module uses internally is undocumented and unsupported (Michev;
a Microsoft Q&A answer agrees) and is not built on. EWS is being disabled from 2026-10, fully by 2027-04.

**Consequence:** a tool that covers the mailbox domain runs the Exchange Online PowerShell module, whatever its
own language is; the choice is in-process hosting (.NET) or a sidecar process (any language).

## The no-new-app path

What cuts the PowerShell start-up tax without a new tool, ranked:

1. `Microsoft.Graph.Authentication` plus `Invoke-MgGraphRequest` as a thin client: 0.8 to 1.1 s against 5.9 s for
   three sub-modules, every endpoint reachable, paging and typing by hand. CISA's ScubaGear measured six Beta
   sub-modules at 31 to 68 s on first use and switched to raw requests (issue #1592, closed 2025-02-25).
2. One warm, long-lived host: the import is paid once a day, not once a command. Claude Code's shell tools start
   a process per call, so an agent pays the cold import every time unless a persistent-session MCP server
   (PowerShell.MCP, the PnP MCP server) holds the session.
3. Explicit, minimal sub-module imports; never the `Microsoft.Graph` meta-module. `-Prefix` renames only.
4. A clean install (81 sub-modules, several versions) and a module path outside OneDrive's Known Folder Move,
   which slows module discovery (check `$env:PSModulePath`).
5. Watching the assembly conflicts of Graph, PnP and Exchange Online in one host (Graph SDK issue #2622).

Exchange Online: `-SkipLoadingFormatData` and `-CommandName` cut the connect; the help no longer loads by
default since 3.7; the `Get-EXO*` cmdlets are the fast bulk readers; no published connect-time figure exists.

A personal module (public and private functions, a connection cache, objects out, `SupportsShouldProcess` on
every write, Pester tests that mock `Invoke-MgGraphRequest`; Maester is the best template) solves the scripting
administrator's problem. Handing it to a click-ops team means a menu script or PowerShell Universal
(subscription per server, *price unverified*). Scheduled work belongs in Azure Automation (PowerShell 7,
managed identity, Exchange Online included, 500 free minutes a month).

**Verdict:** mostly unnecessary for the administrator alone; only partly for the team hand-off.

## The agent path

MCP servers over Graph exist in numbers (Lokka, Softeria, okapi-ca/ms-365-admin-mcp with 674 tools and
risk-levelled writes, Microsoft's read-only Enterprise server, the Agent 365 and Work IQ servers in preview);
none wraps Exchange Online, so the agent's Exchange route is a persistent PowerShell session with one's own
cmdlets. The merill/msgraph skill fixes the agent's weak point, guessing Graph URLs and permissions. The agent
is strongest on ad-hoc questions that join several sources and on the audit domain; writes want a read-only
default, a confirmation and a function with `-WhatIf` behind them. Two risks are not technical: personal data
leaving the tenant for the model provider (a data protection question for the employer) and an audit trail that
records the change but not the prompt. The support team cannot run Claude Code; its agent is the Microsoft 365
Admin agent, which has no Exchange scenario.

**Verdict:** partly. It replaces exploration for the administrator; it does not replace the deterministic layer
(repeatable bulk, scheduled runs, Exchange, a change record, the hand-off) and cannot serve the team.

## The verdict

A standalone product is not worth building: the admin centers and the Admin agent serve the click-ops side for
free, the ecosystem covers each task, and the platform churns (the CLI retired, the SDK's v3, the Admin API in
preview, EWS retiring). A thin personal layer is: Graph and Exchange Online under one sign-in and one output,
a warm host, a cross-workload access view and a mailbox reverse lookup (paid features elsewhere), a readable
Teams chat export, and a confirmation-guarded hand-off of Exchange tasks to the team. Decision D-001.

## The stack

Compared for a tool that will be built: Rust with Tauri (the maintainer's proven pattern), a PowerShell module,
.NET 10 with C#, Go, and Node for reference. Scored on start-up, delegated authentication on Windows, Graph
coverage effort, Exchange Online reach, a GUI for click-ops users, a CLI for agents, offline tests, solo
maintainability, pattern reuse and the dependency footprint, Rust scored highest (41 of 50) largely on pattern
reuse and start-up, .NET second (37), Go (35), PowerShell (32). The decisive facts were elsewhere:

- **Exchange Online reach does not separate the stacks:** every one of them runs the module, in-process or as
  a sidecar. Authentication, distribution and maintenance decide.
- **Authentication on Windows:** MSAL.NET 4.91.1 (2026-10-19) with `Microsoft.Identity.Client.Broker` gives the
  Windows account manager (WAM): single sign-on with the signed-in Entra account, Conditional Access including
  token protection, device-bound refresh tokens, a protected cache. Rust has no MSAL: `azure_identity` 1.0.0
  (2026-05-12) ships no delegated credential at all (no device code, no interactive browser, none planned in
  1.1-beta), `oauth2` 5.0.0 does PKCE and device code by hand, WAM would be a WinRT interop spike with no sample,
  and the Credential Manager caps a blob at about 2.5 KB. Go's `azidentity` 1.15.0 has delegated credentials but
  no broker. `graph-rs-sdk` 3.0.1 is stale (2025-04).
- **Exchange Online from .NET** is documented by Microsoft (a C# runspace sample with the module), and
  `Connect-ExchangeOnline -AccessToken` takes a token the host acquired, so one sign-in can serve Graph and the
  module; the in-process route pins the build to `Microsoft.PowerShell.SDK` 7.6 (net10.0 only) and rules out
  NativeAOT, which WAM in a single file breaks anyway (MSAL.NET issue #6232).
- **Costs of .NET:** ~100 to 150 ms start-up with ReadyToRun (*estimate*) against ~10 ms for Rust, a larger
  publish, and none of the Cargo-side pattern reused (the repo standard, the task files, the CI shape and the
  commit rules carry over).

Decision D-002: .NET 10 and C#, a core library, a `System.CommandLine` command line and a window; D-003 the
authentication; D-004 the three-layer Exchange reach with a sidecar first; D-007 the framework-dependent
ReadyToRun publish. The window's technology: Photino.NET 4.0.16 (a WebView2 host with a string bridge,
dormant since 2025-01, net8/net9 targets that load on net10) behind an `IUiHost` seam, with a WinForms and
WebView2 host as the fallback a spike compares (task 014); the front end Vite, React, Tailwind and shadcn/ui.
`System.CommandLine` 2.0.14 (2026-10-25) is GA; 3.0 shipped a release candidate the same day and is a later
migration. xUnit v3 4.x runs on Microsoft.Testing.Platform only.

## Open questions

- Does the tenant enforce Conditional Access policies (token protection, compliant device, authentication
  flows) that block device code or need the broker? Decides the fallback order (task 013).
- Can a public client acquire `Exchange.ManageV2`, and does `Connect-ExchangeOnline -AccessToken` accept a
  WAM-acquired token for `outlook.office365.com`? One sign-in for all three backends hangs on it (tasks 009
  and 010).
- Is the Admin API v2.0 enabled in the tenant ("not available in all organizations")?
- Will the support team's machines have PowerShell 7.6 and the module (Intune)?
- Does the Teams export API (`getAllMessages`, application permission, a protected API) still need the
  request form and metering in 2026? *Unverified.*
- Cold `Connect-ExchangeOnline` time with and without `-CommandName` on the maintainer's machine (task 011).

## Sources

- Graph CLI retirement: https://devblogs.microsoft.com/microsoft365dev/microsoft-graph-cli-retirement/ and
  https://github.com/microsoftgraph/msgraph-cli/issues/585
- Graph PowerShell SDK releases and the import-time issue: https://github.com/microsoftgraph/msgraph-sdk-powershell/releases,
  https://github.com/microsoftgraph/msgraph-sdk-powershell/issues/2808, the v3 plan
  https://devblogs.microsoft.com/microsoft365dev/investing-in-a-more-reliable-microsoft-graph-powershell-experience/
- ScubaGear's measurement: https://github.com/cisagov/ScubaGear/issues/1592
- Exchange Online PowerShell: https://learn.microsoft.com/powershell/exchange/exchange-online-powershell-v2,
  https://learn.microsoft.com/powershell/exchange/connect-to-exo-powershell-c-sharp,
  https://learn.microsoft.com/powershell/module/exchangepowershell/connect-exchangeonline,
  https://www.powershellgallery.com/packages/ExchangeOnlineManagement
- Exchange Online Admin API: https://learn.microsoft.com/exchange/reference/admin-api-overview,
  https://learn.microsoft.com/exchange/reference/admin-api-authentication,
  https://learn.microsoft.com/exchange/reference/admin-api-endpoints-reference,
  https://techcommunity.microsoft.com/blog/exchange/announcing-public-preview-exchange-online-admin-api/4470562;
  the undocumented endpoint: https://michev.info/blog/post/5822/all-you-need-to-know-about-exchange-online-admin-api-or-how-to-run-cmdlets-without-powershell
- EWS retirement: https://learn.microsoft.com/exchange/clients-and-mobile-in-exchange-online/deprecation-of-ews-exchange-online
- Message trace in Graph: https://learn.microsoft.com/graph/api/messagetracingroot-list-messagetraces
- Microsoft 365 Admin agent: https://learn.microsoft.com/microsoft-365/copilot/copilot-ai-admin-agent
- MCP Server for Enterprise: https://learn.microsoft.com/graph/mcp-server/overview; Agent 365 servers:
  https://learn.microsoft.com/microsoft-agent-365/tooling-servers-overview
- Community tools: https://github.com/pnp/cli-microsoft365, https://github.com/pnp/powershell,
  https://github.com/merill/msgraph, https://github.com/merill/lokka, https://github.com/Softeria/ms-365-mcp-server,
  https://github.com/okapi-ca/ms-365-admin-mcp-server, https://github.com/pnp/pnp-powershell-mcp-server,
  https://github.com/yotsuda/PowerShell.MCP, https://github.com/osodevops/ms-teams-cli,
  https://github.com/maester365/maester, https://github.com/admindroid-community/powershell-scripts,
  https://github.com/12Knocksinna/Office365itpros
- Commercial: https://admindroid.com, https://www.manageengine.com/microsoft-365-management-reporting/,
  https://www.syskit.com/pricing/, https://www.coreview.com, https://sharegate.com, https://www.hornetsecurity.com
- Teams export: https://learn.microsoft.com/microsoftteams/export-teams-content,
  https://learn.microsoft.com/graph/api/chats-getallmessages
- Authentication libraries: https://www.nuget.org/packages/Microsoft.Identity.Client,
  https://www.nuget.org/packages/Microsoft.Identity.Client.Broker,
  https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam,
  https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/6232,
  https://docs.rs/crate/azure_identity/latest, https://crates.io/crates/oauth2, https://crates.io/crates/keyring,
  https://github.com/sreeise/graph-rs-sdk, https://pkg.go.dev/github.com/Azure/azure-sdk-for-go/sdk/azidentity
- Stack packages: https://www.nuget.org/packages/System.CommandLine, https://www.nuget.org/packages/Photino.NET,
  https://github.com/tryphotino/photino.NET, https://www.nuget.org/packages/xunit.v3,
  https://www.nuget.org/packages/Microsoft.PowerShell.SDK, https://www.nuget.org/packages/nuget-license
