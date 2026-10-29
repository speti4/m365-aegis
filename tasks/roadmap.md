# Roadmap

A thin Microsoft 365 operations layer for one tenant, Windows 11 first. Order: first the sign-in, the Graph
client and the lookups a day of administration asks for, then Exchange Online through the sidecar, then writes
behind a plan, then the window for the support team, then the snapshot views that no free tool offers, then the
release and the hand-off. State lives in the task files and the generated index, never here. The `## ` headings
are the milestone names a task joins with `milestone:`.

## M1 Foundation

The sign-in through the Windows account manager (`login`, `logout`, `whoami`), the Graph client (paging,
throttling, batching, v1.0 and beta), the output contract (the envelope, the table, the exit codes), the raw
`call`, the `doctor`, and the first lookups of the groups and audit domains (members and owners, groups without
owners, sign-ins, expiring secrets, message trace), all against offline fixtures; the spikes that decide the
Exchange sign-in and the window host. Done when the daily lookups run from a terminal and from a Claude Code
session with a silent sign-in, in well under a second plus the network.

## M2 Exchange

The pwsh sidecar with its allow-list and one sign-in shared with Graph, the mailbox access reverse lookup, the
external forwarders, the shared mailbox list and the mailbox sizes; the Admin API backend where the tenant has
it. Done when the mailbox domain's reads run from the command line after one sign-in.

## M3 Writes with safety

Plans and applies: `--dry-run` prints the plan, `--yes` applies it; group membership and owners, mailbox
permissions, forwarding; bulk changes from a CSV. Done when a bulk mailbox permission grant runs as a dry run,
then an apply, and the tenant's audit log shows exactly the planned changes.

## M4 Window

The window for the support team: a WinForms form with the WebView2 control (D-009) and the React front end,
task cards that end in a plan and a confirmation, a warm sidecar, the sign-in state. Done when a support
colleague grants FullAccess on a shared mailbox from the window without touching PowerShell.

## M5 Snapshot and views

A nightly snapshot of groups, sites, sharing and mailbox permissions, the cross-workload "who can reach what"
view, and the Teams chat export through an app-only certificate profile. Done when the access view answers
for one user from the snapshot in a second.

## M6 Release and team

Signed exes, the usage guide in the zip, the doctor's full check list, distribution through Intune or winget,
the team's onboarding, and the decision on publishing the repo. Done when the support team runs a release from
the zip on their own machines.
