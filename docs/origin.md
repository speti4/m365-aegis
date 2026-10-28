# Origin

m365-aegis started as a greenfield project with one concrete problem behind it and a research round before
the first line of code.

## Starting point

A Microsoft 365 tenant of a mid-sized company, administered by one person who also writes the scripts: groups
and Teams, SharePoint sites and sharing, Exchange Online mailboxes and their permissions, and the audit, sign-in
and usage reports a week of operations asks for. The daily tools are the Microsoft Graph PowerShell SDK, the
Exchange Online PowerShell module, PnP.PowerShell and the admin centers. Next to the administrator sits an IT
support team that lives in the admin centers and rarely runs a script.

## Problem

The scripted path is slow to start and wide. Measured on 2026-10-21 on a Windows 11 workstation, with a cold
`pwsh -NoProfile` taking 0.2 s on its own: importing `Microsoft.Graph.Authentication` takes 0.8 to 1.1 s,
`Microsoft.Graph.Users` 2.3 to 2.7 s, and Users, Groups and Identity.SignIns together 5.9 s; 81 Graph
sub-modules were installed, the Users one alone with 207 cmdlets. Every one-off lookup pays that, and an agent
session that runs each command in a fresh process pays it every time. The Microsoft Graph CLI that would have
been the lean alternative was retired on 2026-08-28 with no command-line successor. The Exchange Online side has
no Graph equivalent for most of what an administrator does with mailboxes (permissions, forwarding, retention,
shared mailboxes, transport rules), so any tool that wants the mailbox domain has to drive the Exchange Online
PowerShell module. And the support team is served by the admin centers and, since 2026-07, by the Microsoft 365
Admin agent for users, groups and Teams, but not for Exchange.

The research of 2026-10-28 ([the landscape page](research/2026-10-28-m365-admin-tooling-landscape.md)) measured
the existing tools against these three domains. Each individual task is covered somewhere: the Graph and
Exchange modules, CLI for Microsoft 365, PnP, the Graph MCP servers, the script collections, the commercial
suites. What no free tool offers is the integration: Graph and Exchange Online under one sign-in and one output
format, a warm process that pays the import once, a cross-workload view of who can reach what, a mailbox
permission lookup in reverse, and a way to hand an Exchange task to the support team with a confirmation step.

## Need

A thin personal operations layer, not a product (decision D-001): a .NET core that signs in once through the
Windows account manager and talks to Graph directly and to Exchange Online through a long-lived PowerShell
sidecar; a command line whose answers a shell, a script or an agent can parse (a JSON envelope, stable exit
codes) and a person can read (a table); and a small window with guided task cards for the support team, every
write behind a plan and a confirmation. Windows 11 first; the tenant's data never in the repository.

## Usage profile

Daily lookups from a terminal and from Claude Code sessions, a few dozen a day, each wanting an answer in well
under a second plus the network. Weekly reports (expiring secrets, external forwarders, groups without owners).
Occasional bulk changes from a CSV, with a dry run first. Later, the support team's Exchange tasks through the
window, a handful a day. One tenant, one administrator at first, a team of a few people later.
