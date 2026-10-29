---
status: idea
size: M
origin: done/010-spike-exchange-online-admin-api-in-the-tenant.md
created: 2026-10-29
milestone: M2 Exchange
---

# The Exchange Online Admin API backend: Send on Behalf, folder permissions, group membership, organization reads

What: the second layer of D-004 as a backend behind the same interface as the sidecar: the broker's token for
`https://outlook.office365.com/.default` (`Exchange.ManageV2`), POST calls with a `CmdletInput` body and
`X-AnchorMailbox: UPN:<admin>` to `adminapi/v2.0/<tenant id>/…`, `@odata.nextLink` paging through the Graph
client's shape; the reads the sidecar answers slowly or not at all: Send on Behalf with display names, mailbox
folder permissions, distribution group membership, the organization configuration; fixtures under
`tests/fixtures/graph/adminapi/`.
Why: task 010 proved on 2026-10-29 that the API answers in the tenant with the broker's token (`OrganizationConfig`
in 0.6 s, `Mailbox` paged); a REST call is one round trip where the sidecar costs a 2.4 s cold connect.
Done when: each of those reads answers from fixtures and live after one sign-in, a `mailbox` command picks the
backend by the read it needs, and `docs/development.md` names which read goes through which layer.
