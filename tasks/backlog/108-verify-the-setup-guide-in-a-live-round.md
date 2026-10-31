---
status: ready
size: S
origin: wip/106-user-facing-readme-and-setup-guide.md
created: 2026-10-31
milestone: M6 Release and team
---

# Verify the setup guide in a live round: the Azure CLI registration block and the least role for the message trace

What: in a signed-in round, run the Azure CLI block of `docs/setup.md` ("With the Azure CLI") against a throwaway
registration in the test tenant and fix whatever differs from the Learn reference it was written from; then find the
least Exchange Online role an account needs for `trace` (the guide's permissions table names none, because Microsoft's
reference names none) and write it into the table.
Why: the guide is the administrator's path into the tool for every other tenant, and its two unverified places are
the ones a stranger cannot check against anything else.
Done when: the CLI block has run end to end once (the registration deleted afterwards), the table names the trace
role, and both carry the date of the round in the task's Log.
Open (2026-11-01, from the close of 096): see how a session without an Exchange role refuses `Get-DistributionGroup`;
a hidden cmdlet ("is not recognized", exit 13) is a shape the fallback of 096 does not catch yet.
