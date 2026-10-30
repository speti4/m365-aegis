@{
    # A stand-in for the real module, so the sidecar's protocol, its connect and its allow-list run offline in the
    # tests (tests/sidecar/sidecar.Tests.ps1 puts this folder first on PSModulePath). Nothing here talks to a
    # tenant; the cmdlets answer with made-up objects for contoso.onmicrosoft.com.
    RootModule = 'ExchangeOnlineManagement.psm1'
    ModuleVersion = '3.10.1'
    GUID = '0f2a7c3e-6b1d-4b2e-9d4a-000000000001'
    Author = 'm365-aegis tests'
    Description = 'Fake ExchangeOnlineManagement for the sidecar tests; no network.'
    PowerShellVersion = '7.6'
    FunctionsToExport = @('Connect-ExchangeOnline', 'Disconnect-ExchangeOnline', 'Get-EXOMailbox', 'Get-EXORecipient', 'Get-EXOMailboxPermission',
        'Get-EXORecipientPermission', 'Get-EXOMailboxStatistics', 'Get-AcceptedDomain', 'Get-EXOMailboxFolderStatistics',
        'Add-MailboxPermission', 'Remove-MailboxPermission', 'Add-RecipientPermission', 'Remove-RecipientPermission', 'Set-Mailbox')
}
