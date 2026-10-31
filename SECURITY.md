# Security policy

m365-aegis signs in as a Microsoft 365 administrator and reads and changes a tenant's groups, mailboxes and
settings. A flaw in how it handles tokens, confirmations or the data it prints matters, and a report is
welcome.

## Supported versions

Only the latest release on the [Releases page](https://github.com/speti4/m365-aegis/releases) gets fixes. A fix
ships as a new release; there are no backports. A report against `main` is welcome too; its fix lands there and
ships with the next release.

## Verifying a release

A release is `m365-aegis-<version>.zip` on the Releases page, with `SHA256SUMS.txt` and its notes. Next to the
exes the zip holds `README.txt` (what the folder holds and these checks in short), `SETUP.md` and `GUIDE.md` (the
administrator's setup and the usage guide, the same files as `docs/setup.md` and `docs/usage.md`), `LICENSE.txt`
and `THIRD-PARTY-NOTICES.md`. Check it three ways before you run it:

- **The checksum.** `(Get-FileHash .\m365-aegis-<version>.zip).Hash` prints the value of `SHA256SUMS.txt` in capitals.
- **The build provenance.** `gh attestation verify .\m365-aegis-<version>.zip --repo speti4/m365-aegis` shows that
  the zip was built by this repository's release workflow from a tag on `main`.
- **The signature.** The exes and the tool's own assemblies (`m365-aegis*.exe`, `m365-aegis*.dll`,
  `M365Aegis.*.dll`) are Authenticode-signed and timestamped, and the release notes name the signer and the
  certificate's thumbprint. In the unzipped folder `Get-AuthenticodeSignature .\m365-aegis.exe` (or `signtool verify
  /pa`) shows the signer, `(Get-AuthenticodeSignature .\m365-aegis.exe).SignerCertificate.Thumbprint` its
  thumbprint, and `m365-aegis doctor` checks every signed file in its `signature` row, which passes only for the
  certificate whose thumbprint the tool pins, the one the release notes print, and warns with the signer and its
  thumbprint for any other. The signing certificate's key is a secret of the release workflow, never in the
  repository.

A file of a release zip whose signature does not hold, a release zip that fails its attestation, or a way to make the
release workflow sign something other than the build of the tag, is in scope.

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
  `outlook.office365.com` only. A token written to a log, a plan record, an envelope, stderr or a message of the
  window's bridge to its page (a reply or a notification), a token sent to any other host, a downgrade to plain HTTP, or a cache readable by another user is a vulnerability. The tool creates
  its home folder readable by the signed-in user alone, and `m365-aegis doctor` warns when an existing one is
  readable by `Everyone`, `Users` or `Authenticated Users`.
- **Writes.** Every change to the tenant goes through a plan and needs `--yes` on the command line or a
  confirmation in the window (decision D-006 in `tasks/decisions/`): a write card's "Confirm and apply", which sends
  the bridge's `apply-plan` with `"confirm": true` for a plan the window computed and holds, applied once. A write
  that happens without one, an apply of a plan the window did not show, or a plan that shows one change and applies
  another, is a vulnerability.
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

- SmartScreen's warning on the first start of a release whose signature has no reputation yet (that is Microsoft's
  to build), and a signing certificate that a machine does not trust until its administrator trusts it.
- What an administrator with the signed-in roles may do in the tenant anyway: the tool acts with the user's own
  permissions and adds none.
- Anything that needs an attacker who can already run code as the signed-in user on the same machine.
- Bugs in Microsoft Graph, Exchange Online, WebView2, the .NET runtime or Windows itself. Report those to
  Microsoft. A way m365-aegis could defend against one is welcome as an ordinary issue.
