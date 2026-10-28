---
status: decided
date: 2026-10-28
cost: medium
sources: [wip/001-project-scaffolding.md, docs/research/2026-10-28-m365-admin-tooling-landscape.md, docs/origin.md]
---

# D-001 — Build a thin personal operations layer, not a product

## Context

One administrator of one Microsoft 365 tenant, three domains of daily work (groups, Teams and SharePoint;
Exchange Online mailboxes; audit, sign-ins and reports), a support team that works in the admin centers. The
scripted path pays a module import of one to six seconds per command, the Microsoft Graph CLI was retired on
2026-08-28 with no successor, and the mailbox domain exists only in Exchange Online PowerShell. The research of
2026-10-28 (`docs/research/2026-10-28-m365-admin-tooling-landscape.md`) measured the market, the no-new-app path
and the agent path against the three domains before any code.

## Decision

Build a thin personal operations layer and not a product: a core, a command line for the administrator and
agents, and a small window with guided task cards for the support team. The value is integration, not
capability: Graph and Exchange Online under one sign-in and one output contract, a warm process that pays the
import once, a cross-workload view of who can reach what, a mailbox permission lookup in reverse, a readable
Teams chat export, and a confirmation-guarded hand-off of Exchange tasks to the team. Each individual task stays
available in the existing tools; the layer wraps the daily ones. The cost is medium: one milestone of work
before it is useful, and the research stands on its own if the project stops. Decided 2026-10-28.

## Not chosen

- A standalone product for the click-ops side: the admin centers and the Microsoft 365 Admin agent (GA 2026-07,
  no extra license) serve it for free, Exchange excepted, and the platform churns (the CLI retired, the Graph
  SDK's v3, the Admin API in preview, EWS retiring).
- Only a PowerShell module with a warm MCP session: it fixes the administrator's start-up tax and nothing of the
  team hand-off; a module cannot be given to a click-ops team without a portal or a GUI.
- Only the existing tools: the Graph thin client (`Microsoft.Graph.Authentication` plus `Invoke-MgGraphRequest`),
  the script collections and AdminDroid's free edition cover each task, none covers the integration.
- Only the agent path: it replaces exploration, not the deterministic layer (repeatable bulk, Exchange, a change
  record, scheduled runs), and the support team cannot run it.
