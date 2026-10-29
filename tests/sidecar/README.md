# The sidecar tests and the fake module

`sidecar.Tests.ps1` (Pester) starts `src/M365Aegis.Core/Exchange/Sidecar/aegis-exo-sidecar.ps1` as a real pwsh child
and speaks its protocol line by line; the Core tests (`tests/M365Aegis.Core.Tests/Exchange`) drive the same script
through the C# client and session, and the Cli tests (`MailboxCommandTests`) through the built exe. All of them set
`M365AEGIS_EXO_MODULE_ROOT` to `modules/`, so the script finds the fake `ExchangeOnlineManagement` there and never the
machine's real module; nothing reaches a tenant (docs/development.md, "The Exchange Online sidecar").

`modules/ExchangeOnlineManagement` has the real cmdlet names and parameter shapes and made-up answers for
`contoso.onmicrosoft.com`: `Connect-ExchangeOnline` (any token for that organization, with `-Organization` or `-UserPrincipalName`;
`fabrikam.onmicrosoft.com` is refused, `expired-token` is a 401, `refuse.contoso.onmicrosoft.com` refuses with the
token in its message, which the script must scrub), `Disconnect-ExchangeOnline` (the connect writes the variant it got, `-SkipLoadingFormatData` and `-CommandName`, to
the console, which is the sidecar's stderr), and the seven cmdlets of the
allow-list: `Get-EXOMailbox`, `Get-EXOMailboxPermission`, `Get-EXORecipientPermission`, `Get-EXOMailboxStatistics`,
`Get-EXORecipient`, `Get-AcceptedDomain` (`contoso.com`, `contoso.onmicrosoft.com`) and `Get-EXOMailboxFolderStatistics`. The permission cmdlets accept
piped identities, as the real ones do.

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

```powershell
Invoke-Pester -Path tests/sidecar
```
