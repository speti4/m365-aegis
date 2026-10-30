# Security policy

m365-aegis signs in as a Microsoft 365 administrator and reads and, later, changes a tenant's groups, mailboxes
and settings. A flaw in how it handles tokens, confirmations or the data it prints matters, and a report is
welcome.

## Supported versions

Only the latest release on the [Releases page](https://github.com/speti4/m365-aegis/releases) gets fixes. A fix
ships as a new release; there are no backports. There is no release yet: until the first one, reports against
`main` are welcome and the fix lands there.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting:
[Report a vulnerability](https://github.com/speti4/m365-aegis/security/advisories/new) (the *Security* tab →
*Report a vulnerability*). Please do not open a public issue for a security problem.

Never attach a tenant's data to a report: no tenant name or id, no user or mailbox names, no object ids, no
sign-in records and never a token. Describe the problem with made-up names (`contoso.onmicrosoft.com`,
`alice@contoso.com`); the fixtures under `tests/fixtures/` show the shapes that reproduce almost anything.

You can expect a first answer within a week. Once a fix is released, the advisory is published with credit to
you unless you ask otherwise.

## What is in scope

The places where m365-aegis handles credentials, untrusted input or writes:

- **Sign-in and tokens.** Tokens come from the Windows account manager (WAM) or the MSAL cache under
  `%LOCALAPPDATA%\m365-aegis\`, and are sent to `login.microsoftonline.com`, `graph.microsoft.com` and
  `outlook.office365.com` only. A token written to a log, a plan record, an envelope or stderr, a token sent to any
  other host, a downgrade to plain HTTP, or a cache readable by another user is a vulnerability. The tool creates
  its home folder readable by the signed-in user alone, and `m365-aegis doctor` warns when an existing one is
  readable by `Everyone`, `Users` or `Authenticated Users`.
- **Writes.** Every change to the tenant goes through a plan and needs `--yes` on the command line or a
  confirmation in the window (decision D-006 in `tasks/decisions/`). A write that happens without one, or a plan
  that shows one change and applies another, is a vulnerability.
- **The Exchange Online sidecar.** The command line and the window start `pwsh` with the sidecar script and drive
  it over JSON lines; only the cmdlets on its allow-list run. A way to run another cmdlet, to run a script of the
  caller's choosing, or to make the sidecar connect to a tenant other than the one signed in to is in scope. The
  script's hash is checked before it is started.
- **The window.** The page is the front end built from `gui/web/` and loaded from the exe's own folder; it talks
  to the host through the message bridge only. Loading a page from elsewhere, a link opened in the web view
  instead of the system browser, or a bridge message that bypasses the plan and confirmation is in scope.
- **Output.** The JSON envelope and the table are read by scripts and people; an answer that mixes a service's
  text into a field it does not belong to, so a caller acts on it, is in scope.

## Out of scope

- The exes are not code-signed yet, so SmartScreen warns on the first start (signing is a milestone of its own).
- What an administrator with the signed-in roles may do in the tenant anyway: the tool acts with the user's own
  permissions and adds none.
- Anything that needs an attacker who can already run code as the signed-in user on the same machine.
- Bugs in Microsoft Graph, Exchange Online, WebView2, the .NET runtime or Windows itself. Report those to
  Microsoft. A way m365-aegis could defend against one is welcome as an ordinary issue.
