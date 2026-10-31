# The sidecar tests and the fake module

`sidecar.Tests.ps1` (Pester) starts `src/M365Aegis.Core/Exchange/Sidecar/aegis-exo-sidecar.ps1` as a real pwsh child
and speaks its protocol line by line; the Core tests (`tests/M365Aegis.Core.Tests/Exchange`) drive the same script
through the C# client and session, and the Cli tests (`MailboxCommandTests`) through the built exe. All of them set
`M365AEGIS_EXO_MODULE_ROOT` to `modules/`, so the script finds the fake `ExchangeOnlineManagement` there and never the
machine's real module; nothing reaches a tenant (docs/development.md, "The Exchange Online sidecar").

Under a test module root the script turns pwsh's module auto-loading off (`$PSModuleAutoLoadingPreference = 'None'`,
after importing the two built-in modules it uses itself; task 050). Without it, a cmdlet the fake module does not
export is found in the machine's real `ExchangeOnlineManagement` and imported beside the fake whenever the two differ
in version, which made a test call the real module once (2026-10-30). Two Pester tests hold the guard: the fake
manifest's `FunctionsToExport` equals the allow-list plus `Connect-ExchangeOnline` and `Disconnect-ExchangeOnline`
(the failure names the missing cmdlet, so a cmdlet added to the allow-list is added to the manifest in the same
change), and a copy of the fake without one export, in another version, answers that the cmdlet is not recognized.

`modules/ExchangeOnlineManagement` has the real cmdlet names and parameter shapes and made-up answers for
`contoso.onmicrosoft.com`: `Connect-ExchangeOnline` (any token for that organization, with `-Organization` or `-UserPrincipalName`;
`fabrikam.onmicrosoft.com` is refused, `expired-token` is a 401, `refuse.contoso.onmicrosoft.com` refuses with the
token in its message, which the script must scrub), `Disconnect-ExchangeOnline` (it fails after a connect with
`disconnect-fails-token`, task 070; the connect writes the variant it got, `-SkipLoadingFormatData` and `-CommandName`, to
the console, which is the sidecar's stderr), and the seven read cmdlets of the
allow-list: `Get-EXOMailbox`, `Get-EXOMailboxPermission`, `Get-EXORecipientPermission`, `Get-EXOMailboxStatistics`,
`Get-EXORecipient`, `Get-AcceptedDomain` (`contoso.com`, `contoso.onmicrosoft.com`) and `Get-EXOMailboxFolderStatistics`. The permission cmdlets accept
piped identities, as the real ones do.

The write cmdlets of the allow-list (task 053), `Add-MailboxPermission`, `Remove-MailboxPermission`,
`Add-RecipientPermission`, `Remove-RecipientPermission` and `Set-Mailbox` (`-GrantSendOnBehalfTo` as `@{Add=…}` or
`@{Remove=…}`, `-ForwardingSmtpAddress`, `-ForwardingAddress`, `-DeliverToMailboxAndForward`), change the made-up
data of the process they run in, so a read after a write sees it, and append each call as one JSON line
(`{"cmdlet","parameters"}`, without `Confirm` and the common parameters) to the file `M365AEGIS_FAKE_EXO_WRITES`
names, when it is set, which is how a test sees what an apply sent. `Add-MailboxPermission` also takes
`-AutoMapping` (a boolean, as the real one; task 059), which the record carries when it is given. A holder is a mailbox (named in an entry by its
address) or the group Finance Staff (by its name); a right for the mail contact Dave Private fails as the service's
does (no security principal, a non-terminating error, exit code 13), and so does the revoke of a right nobody holds.
The removals declare `ConfirmImpact = 'High'`, as the real ones do, so a write sent without `-Confirm:$false` would
stop at a prompt; the failing identities `broken`, `nobody` and `busy` fail the writes too.

| mailbox | type | object id | forwarding | rights on it | size |
| --- | --- | --- | --- | --- | --- |
| alice@contoso.com | UserMailbox | `…011` | none | SELF only | 1.2 GB, 5120 items |
| bob@contoso.com | UserMailbox | `…012` | `bob@fabrikam.com` (external), keeps a copy | carol is denied FullAccess | 500 MB |
| carol@contoso.com | UserMailbox | `…013` | the Finance mailbox (internal) | | 10 MB |
| dave@contoso.com | UserMailbox | `…014` | the mail contact "Dave Private", `dave@example.com` (external) | | empty |
| finance@contoso.com | SharedMailbox | `…015` | none | FullAccess alice, Send As alice, Send on Behalf "Bob Example" | 3 GB |
| reception@contoso.com | SharedMailbox | `…016` | none | FullAccess alice, bob and the group Finance Staff, Send As bob | 100 MB |

`Get-EXORecipient` also knows the mail-enabled security group Finance Staff (`finance-staff@contoso.com`, object id
`…035`, whose members the Graph fixtures hold; task 044), and `Get-EXOMailboxPermission` without an identity
refuses to run, as the real one does (task 043). `Get-EXOMailboxFolderStatistics -FolderScope Calendar` names each
mailbox's calendar folders: the default one is `Calendar`, but Reception's is the localized `Kalender` (task 042). The ids are `00000000-0000-0000-0000-0000000000NN` (the `ExchangeGuid` of each mailbox is `…04N`); every list
also carries the `NT AUTHORITY\…` and inherited entries the real service returns, which the lookups leave out.
Three identities fail on purpose: `broken` (a terminating error, exit code 13), `nobody` (not found, a
non-terminating error, exit code 4) and `busy` (throttled, exit code 5). `scripts/check-fixtures.py` holds this
folder to the same rules as `tests/fixtures`.

The member writes of task 061, `Add-DistributionGroupMember` and `Remove-DistributionGroupMember` (`-Identity`,
`-Member`, `-BypassSecurityGroupManagerCheck`; the removal declares `ConfirmImpact = 'High'`), change two made-up
groups the fake keeps by the members' addresses: the distribution group Sales (`sales@contoso.com`, object id `…036`:
bob, the mail contact `dave@example.com` and alice) and the mail-enabled security group Finance Staff
(`finance-staff@contoso.com`, `…035`: carol). A group is named by its name, alias, address or object id, a member by
anything `Get-EXORecipient` knows; adding a member who is one already, removing one who is not, and a member no
recipient matches fail as the service's do (non-terminating errors), and each call that changed a group is recorded in
the `M365AEGIS_FAKE_EXO_WRITES` file like the other writes.

The owners of task 064: `Get-DistributionGroup` answers the groups with `ManagedBy`, the owners by each recipient's
name as the service lists them (Sales: `alice`; Finance Staff: `carol` and `Organization Management`, a role group no
recipient lookup knows; task 089 adds the distribution group All Staff, `allstaff@contoso.com`, `…03b`, with an empty
`ManagedBy`, and the mail-enabled security group Sales Managers, `salesmanagers@contoso.com`, `…03d`, managed by
`alice`, the two groups the Graph fixtures list without an owner; without `-Identity` it answers every group up to
`-ResultSize`, as `group owners-missing --all-types` reads them), and `Set-DistributionGroup` (`-Identity`, `-ManagedBy @{Add=…}` or `@{Remove=…}`,
`-BypassSecurityGroupManagerCheck`) changes that list, each owner a recipient `Get-EXORecipient` knows; removing an
owner who is not one, or an owner no recipient matches, fails as the service's does (a non-terminating error), and
each call that changed a group is recorded like the other writes.

```powershell
Invoke-Pester -Path tests/sidecar
```
