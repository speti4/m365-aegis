---
status: idea
size: S
origin: wip/086-usage-guide-for-the-command-line-and-the-window.md
created: 2026-10-30
milestone: M6 Release and team
---

# `scripts/check-fixtures.py` catches two-label domains too

What: the `ANY_DOMAIN` pattern of the fixture scan asks for three labels, so `alice@realcompany.com` or
`realcompany.hu` passes the check today; the pattern accepts two labels, the allow-list names the domains the repo may
carry (`contoso.com`, `fabrikam.com`, Microsoft's endpoints, the package registries, the docs' links), and whatever the
tightened scan flags in the fixtures, the docs and the guide is cleaned or allow-listed with a reason.
Why: the repo is public (D-010) and the scan is the gate that keeps tenant data out; a two-label domain is the common
shape of a real tenant's domain.
Done when: a test fixture with a two-label domain fails the scan, the repo passes it with 0 findings, and the
allow-list's entries each carry a one-line reason.
