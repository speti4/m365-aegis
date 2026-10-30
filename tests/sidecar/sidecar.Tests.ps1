#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.7.0' }
<#
The Exchange Online sidecar end to end: a real pwsh child process running aegis-exo-sidecar.ps1 with the fake
ExchangeOnlineManagement module (M365AEGIS_EXO_MODULE_ROOT). Every answer is read back as a protocol line; the fake
module's Write-Host and console noise must not appear among them. Runs on Linux and Windows alike (the CI
`sidecar` job and the local checks of CONTRIBUTING.md). The Core's own tests drive the same script through the C#
protocol client (tests/M365Aegis.Core.Tests/Exchange).
#>

BeforeAll {
    $script:Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Sidecar = Join-Path $script:Repo 'src' 'M365Aegis.Core' 'Exchange' 'Sidecar' 'aegis-exo-sidecar.ps1'
    $script:FakeModules = Join-Path $PSScriptRoot 'modules' 'ExchangeOnlineManagement'

    function Invoke-Sidecar {
        # Feeds the lines to a fresh sidecar and returns its stdout lines (the protocol) and stderr (the noise).
        param([string[]] $Lines, [switch] $WithoutModule, [string] $ModuleRoot, [hashtable] $Environment = @{})
        $moduleRoot = if ($ModuleRoot) { $ModuleRoot } elseif ($WithoutModule) { Join-Path $PSScriptRoot 'no-such-folder' } else { Split-Path $script:FakeModules -Parent }
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = (Get-Process -Id $PID).Path
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $script:Sidecar)) { $startInfo.ArgumentList.Add($argument) }
        $startInfo.RedirectStandardInput = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.UseShellExecute = $false
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        $startInfo.StandardInputEncoding = $utf8
        $startInfo.StandardOutputEncoding = $utf8
        # The sidecar looks for ExchangeOnlineManagement under this root only, so the machine's real module (if any)
        # is never imported by a test and a missing module is a deterministic case.
        $startInfo.Environment['M365AEGIS_EXO_MODULE_ROOT'] = $moduleRoot
        foreach ($name in $Environment.Keys) { $startInfo.Environment[$name] = $Environment[$name] }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        foreach ($line in $Lines) { $process.StandardInput.Write($line + "`n") }
        $process.StandardInput.Write('{"aegis":1,"type":"exit"}' + "`n")
        $process.StandardInput.Close()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $stdout = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Raw = $stdout
            Lines = @($stdout -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
            Stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }

    function Get-InvokeLine {
        param([string] $Cmdlet, [hashtable] $Parameters, [string[]] $Properties, [object[]] $InputObject, [string] $Id = '3')
        $data = [ordered]@{ cmdlet = $Cmdlet }
        if ($Parameters) { $data['parameters'] = $Parameters }
        if ($Properties) { $data['properties'] = $Properties }
        if ($InputObject) { $data['input'] = $InputObject }
        [ordered]@{ aegis = 1; id = $Id; type = 'invoke'; data = $data } | ConvertTo-Json -Compress -Depth 5
    }

    $script:Connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","organization":"contoso.onmicrosoft.com"}}'
}

Describe 'the sidecar protocol' {
    It 'answers hello with the pwsh version, the module version and the allow-list' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"1","type":"hello"}')
        $run.ExitCode | Should -Be 0
        $run.Lines.Count | Should -Be 1
        $run.Lines[0].type | Should -Be 'hello'
        $run.Lines[0].id | Should -Be '1'
        $run.Lines[0].aegis | Should -Be 1
        $run.Lines[0].data.pwsh | Should -Match '^7\.'
        $run.Lines[0].data.module | Should -Be '3.10.1'
        $run.Lines[0].data.allowedCmdlets | Should -Contain 'Get-EXOMailbox'
    }

    It 'refuses foreign text without answering it' {
        $run = Invoke-Sidecar -Lines @('not json', '{"aegis":2,"type":"hello"}', '{"aegis":1,"id":"1","type":"hello"}')
        $run.Lines.Count | Should -Be 1
        $run.Stderr | Should -Match 'not a message'
        $run.Stderr | Should -Match 'foreign message refused'
    }

    It 'names an unknown type' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"9","type":"frobnicate"}')
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'unknown-type'
    }

    It 'stops at exit without answering it' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"type":"exit"}', '{"aegis":1,"id":"1","type":"hello"}')
        $run.ExitCode | Should -Be 0
        $run.Lines.Count | Should -Be 0
    }
}

Describe 'connect and reconnect against the fake module' {
    It 'connects and keeps the module noise off the protocol channel' {
        $run = Invoke-Sidecar -Lines @($script:Connect)
        $run.Lines.Count | Should -Be 1
        $run.Lines[0].type | Should -Be 'connected'
        $run.Lines[0].data.organization | Should -Be 'contoso.onmicrosoft.com'
        $run.Lines[0].data.module | Should -Be '3.10.1'
        $run.Lines[0].data.connectMs | Should -BeGreaterOrEqual 0
        $run.Raw | Should -Not -Match 'fake ExchangeOnlineManagement'
        $run.Stderr | Should -Match 'a stray console line'
    }

    It 'connects with a user principal name instead of the organization' {
        $connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","userPrincipalName":"alice@contoso.com"}}'
        $run = Invoke-Sidecar -Lines @($connect)
        $run.Lines[0].type | Should -Be 'connected'
        $run.Lines[0].data.userPrincipalName | Should -Be 'alice@contoso.com'
    }

    It 'reconnects with a fresh token and keeps answering' {
        $reconnect = '{"aegis":1,"id":"4","type":"reconnect","data":{"accessToken":"fresh-token","organization":"contoso.onmicrosoft.com"}}'
        $run = Invoke-Sidecar -Lines @($script:Connect, $reconnect, (Get-InvokeLine -Cmdlet 'Get-AcceptedDomain' -Id '5'))
        $run.Lines.type | Should -Be @('connected', 'connected', 'result')
        $run.Lines[2].data.count | Should -Be 2
    }

    It 'connects with the default variant: no format data, only the REST cmdlets of the allow-list' {
        $run = Invoke-Sidecar -Lines @($script:Connect)
        # The write cmdlets of task 053 are REST cmdlets too, imported by name with the connect.
        $run.Stderr | Should -Match 'fake connect: SkipLoadingFormatData=True CommandName=Get-AcceptedDomain,Add-MailboxPermission,Remove-MailboxPermission,Add-RecipientPermission,Remove-RecipientPermission,Set-Mailbox'
        $run.Lines[0].data.PSObject.Properties.Name | Should -Not -Contain 'missing'
    }

    It 'connects with the variant the measurement names and reports the cmdlets it does not answer' {
        $connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","organization":"contoso.onmicrosoft.com","skipLoadingFormatData":false,"commandName":false,"checkCommands":true}}'
        $run = Invoke-Sidecar -Lines @($connect)
        $run.Stderr | Should -Match '(?m)fake connect: SkipLoadingFormatData=False CommandName=\r?$'
        $run.Lines[0].type | Should -Be 'connected'
        @($run.Lines[0].data.missing).Count | Should -Be 0
    }

    It 'names where the module is in hello' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"1","type":"hello"}')
        $run.Lines[0].data.moduleBase | Should -Match 'ExchangeOnlineManagement'
        $run.Lines[0].data.modulePath | Should -Not -BeNullOrEmpty
    }

    It 'refuses a connect without a token' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"2","type":"connect","data":{"organization":"contoso.onmicrosoft.com"}}')
        $run.Lines[0].data.code | Should -Be 'bad-request'
    }

    It 'reports a missing module instead of connecting' {
        $run = Invoke-Sidecar -Lines @($script:Connect) -WithoutModule
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'module-missing'
    }

    It 'turns a refused connect into connect-failed' {
        $connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","organization":"fabrikam.onmicrosoft.com"}}'
        $run = Invoke-Sidecar -Lines @($connect)
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'connect-failed'
        $run.Lines[0].data.message | Should -Match 'unknown organization'
    }

    It 'never echoes the token in an error, plain or JWT-shaped' -ForEach @(
        @{ Token = 'plain-secret-token' }
        # Built at run time, so the fixture check never meets a JWT-shaped literal in this file.
        @{ Token = (('e' + 'yJ0eXAiOiJKV1QifQ'), ('e' + 'yJzdWIiOiIxIn0'), 'c2ln') -join '.' }
    ) {
        $connect = [ordered]@{ aegis = 1; id = '2'; type = 'connect'; data = @{ accessToken = $Token; organization = 'refuse.contoso.onmicrosoft.com' } } | ConvertTo-Json -Compress
        $run = Invoke-Sidecar -Lines @($connect)
        $run.Lines[0].data.code | Should -Be 'connect-failed'
        $run.Lines[0].data.message | Should -Match '<token>'
        $run.Raw.Contains($Token) | Should -BeFalse
    }
}

Describe 'invoke against the fake module' {
    It 'runs an allowed cmdlet and returns the chosen properties as plain values' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'finance' } -Properties @('DisplayName', 'PrimarySmtpAddress', 'GrantSendOnBehalfTo', 'DeliverToMailboxAndForward', 'Missing')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines.Count | Should -Be 2
        $run.Lines[1].type | Should -Be 'result'
        $run.Lines[1].data.count | Should -Be 1
        $item = $run.Lines[1].data.items[0]
        $item.PrimarySmtpAddress | Should -Be 'finance@contoso.com'
        @($item.GrantSendOnBehalfTo) | Should -Be @('Bob Example')
        $item.DeliverToMailboxAndForward | Should -BeFalse
        $item.PSObject.Properties.Name | Should -Contain 'Missing'
        $item.PSObject.Properties.Name | Should -Not -Contain 'Alias'
    }

    It 'pipes the input into the cmdlet' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxStatistics' -InputObject @('alice', 'bob') -Properties @('DisplayName', 'TotalItemSize')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].data.count | Should -Be 2
        $run.Lines[1].data.items[0].TotalItemSize | Should -Match '\(1,288,490,188 bytes\)$'
    }

    It 'keeps the objects of a bulk run and lists the errors next to them' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxPermission' -InputObject @('finance', 'nobody')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].type | Should -Be 'result'
        $run.Lines[1].data.count | Should -Be 2
        @($run.Lines[1].data.errors).Count | Should -Be 1
        $run.Lines[1].data.errors[0] | Should -Match "couldn't be found"
    }

    It 'refuses the permission list without an identity, as the real module does (task 043)' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxPermission' -Parameters @{ ResultSize = 'Unlimited' }
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[1].data.message | Should -Match 'Identity is a mandatory value'
    }

    It 'lists a group holder among a mailbox''s permission entries (task 044)' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxPermission' -InputObject @('reception') -Properties @('Identity', 'User')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        @($run.Lines[1].data.items | Where-Object { $_.User -eq 'Finance Staff' }).Count | Should -Be 1
    }

    It 'resolves a group holder to a recipient with its object id (task 044)' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXORecipient' -InputObject @('Finance Staff') -Properties @('RecipientTypeDetails', 'ExternalDirectoryObjectId')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].data.items[0].RecipientTypeDetails | Should -Be 'MailUniversalSecurityGroup'
        $run.Lines[1].data.items[0].ExternalDirectoryObjectId | Should -Be '00000000-0000-0000-0000-000000000035'
    }

    It 'names the default calendar in the mailbox''s language (task 042)' {
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxFolderStatistics' -Parameters @{ Identity = 'reception'; FolderScope = 'Calendar' } -Properties @('FolderPath', 'FolderType')
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].type | Should -Be 'result'
        @($run.Lines[1].data.items | Where-Object { $_.FolderType -eq 'Calendar' }).FolderPath | Should -Be '/Kalender'
    }

    It 'refuses a cmdlet that is not on the allow-list' {
        $run = Invoke-Sidecar -Lines @($script:Connect, (Get-InvokeLine -Cmdlet 'Remove-Mailbox' -Parameters @{ Identity = 'alice' }))
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-not-allowed'
    }

    It 'refuses to invoke before a connect' {
        $run = Invoke-Sidecar -Lines @((Get-InvokeLine -Cmdlet 'Get-EXOMailbox'))
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'not-connected'
    }

    It 'turns a terminating cmdlet failure into cmdlet-failed' {
        $run = Invoke-Sidecar -Lines @($script:Connect, (Get-InvokeLine -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'broken' }))
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[1].data.message | Should -Match 'failed on purpose'
    }

    It 'turns a lookup that found nothing into cmdlet-failed with the error id' {
        $run = Invoke-Sidecar -Lines @($script:Connect, (Get-InvokeLine -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'nobody' }))
        $run.Lines[1].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[1].data.message | Should -Match "couldn't be found"
        $run.Lines[1].data.errorId | Should -Match 'ManagementObjectNotFoundException'
    }

    It 'carries accented names through as UTF-8' {
        $name = "Zo`u{00EB} `u{00D6}tv`u{00F6}s"
        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = $name }
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].data.message | Should -Match $name
    }
}

Describe 'the write cmdlets against the fake module (task 053)' {
    BeforeEach {
        $script:Writes = Join-Path ([System.IO.Path]::GetTempPath()) "aegis-sidecar-writes-$([guid]::NewGuid().ToString('N')).jsonl"
    }

    AfterEach {
        Remove-Item -LiteralPath $script:Writes -ErrorAction SilentlyContinue
    }

    It 'grants FullAccess, which a later read lists, and the fake records the call' {
        $grant = Get-InvokeLine -Id '3' -Cmdlet 'Add-MailboxPermission' -Parameters @{ Identity = 'finance@contoso.com'; User = 'bob@contoso.com'; AccessRights = @('FullAccess'); InheritanceType = 'All'; Confirm = $false }
        $read = Get-InvokeLine -Id '4' -Cmdlet 'Get-EXOMailboxPermission' -Parameters @{ Identity = 'finance' } -Properties @('User', 'IsInherited')
        $run = Invoke-Sidecar -Lines @($script:Connect, $grant, $read) -Environment @{ M365AEGIS_FAKE_EXO_WRITES = $script:Writes }
        $run.Lines[1].type | Should -Be 'result'
        @($run.Lines[2].data.items | Where-Object { $_.User -eq 'bob@contoso.com' }).Count | Should -Be 1
        $logged = @(Get-Content -LiteralPath $script:Writes | ForEach-Object { $_ | ConvertFrom-Json })
        $logged.Count | Should -Be 1
        $logged[0].cmdlet | Should -Be 'Add-MailboxPermission'
        $logged[0].parameters.User | Should -Be 'bob@contoso.com'
        $logged[0].parameters.PSObject.Properties.Name | Should -Not -Contain 'Confirm'
    }

    It 'adds and removes a Send on Behalf delegate through the multi-valued change of Set-Mailbox' {
        $add = Get-InvokeLine -Id '3' -Cmdlet 'Set-Mailbox' -Parameters @{ Identity = 'reception@contoso.com'; GrantSendOnBehalfTo = @{ Add = @('carol@contoso.com') }; Confirm = $false }
        $read = Get-InvokeLine -Id '4' -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'reception' } -Properties @('GrantSendOnBehalfTo')
        $remove = Get-InvokeLine -Id '5' -Cmdlet 'Set-Mailbox' -Parameters @{ Identity = 'reception@contoso.com'; GrantSendOnBehalfTo = @{ Remove = @('carol@contoso.com') }; Confirm = $false }
        $again = Get-InvokeLine -Id '6' -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'reception' } -Properties @('GrantSendOnBehalfTo')
        $run = Invoke-Sidecar -Lines @($script:Connect, $add, $read, $remove, $again)
        $run.Lines[1].type | Should -Be 'result'
        @($run.Lines[2].data.items[0].GrantSendOnBehalfTo) | Should -Be @('Carol Example')
        $run.Lines[3].type | Should -Be 'result'
        @($run.Lines[4].data.items[0].GrantSendOnBehalfTo | Where-Object { $_ }).Count | Should -Be 0
    }

    It 'sets and clears the forwarding' {
        $set = Get-InvokeLine -Id '3' -Cmdlet 'Set-Mailbox' -Parameters @{ Identity = 'alice@contoso.com'; ForwardingSmtpAddress = 'alice@fabrikam.com'; DeliverToMailboxAndForward = $true; Confirm = $false }
        $read = Get-InvokeLine -Id '4' -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'alice' } -Properties @('ForwardingSmtpAddress', 'DeliverToMailboxAndForward')
        $clear = Get-InvokeLine -Id '5' -Cmdlet 'Set-Mailbox' -Parameters @{ Identity = 'alice@contoso.com'; ForwardingSmtpAddress = $null; ForwardingAddress = $null; DeliverToMailboxAndForward = $false; Confirm = $false }
        $again = Get-InvokeLine -Id '6' -Cmdlet 'Get-EXOMailbox' -Parameters @{ Identity = 'alice' } -Properties @('ForwardingSmtpAddress')
        $run = Invoke-Sidecar -Lines @($script:Connect, $set, $read, $clear, $again)
        $run.Lines[2].data.items[0].ForwardingSmtpAddress | Should -Be 'smtp:alice@fabrikam.com'
        $run.Lines[2].data.items[0].DeliverToMailboxAndForward | Should -BeTrue
        $run.Lines[4].data.items[0].ForwardingSmtpAddress | Should -BeNullOrEmpty
    }

    It 'refuses a write cmdlet''s parameter off the allow-list before it runs' {
        $invoke = Get-InvokeLine -Cmdlet 'Set-Mailbox' -Parameters @{ Identity = 'alice@contoso.com'; HiddenFromAddressListsEnabled = $true }
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke) -Environment @{ M365AEGIS_FAKE_EXO_WRITES = $script:Writes }
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-not-allowed'
        $run.Lines[1].data.message | Should -Match 'HiddenFromAddressListsEnabled'
        Test-Path -LiteralPath $script:Writes | Should -BeFalse
    }

    It 'fails a grant to the mail contact, which is no security principal' {
        $invoke = Get-InvokeLine -Cmdlet 'Add-RecipientPermission' -Parameters @{ Identity = 'finance@contoso.com'; Trustee = 'dave@example.com'; AccessRights = @('SendAs'); Confirm = $false }
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[1].data.message | Should -Match 'valid security principal'
    }

    It 'fails the revoke of a right nobody holds, and a write on an identity that fails on purpose' -ForEach @(
        @{ Cmdlet = 'Remove-MailboxPermission'; Parameters = @{ Identity = 'alice@contoso.com'; User = 'bob@contoso.com'; AccessRights = @('FullAccess'); InheritanceType = 'All'; Confirm = $false }; Pattern = "ACE doesn't exist" }
        @{ Cmdlet = 'Add-MailboxPermission'; Parameters = @{ Identity = 'busy'; User = 'bob@contoso.com'; AccessRights = @('FullAccess'); InheritanceType = 'All'; Confirm = $false }; Pattern = 'throttled' }
    ) {
        $run = Invoke-Sidecar -Lines @($script:Connect, (Get-InvokeLine -Cmdlet $Cmdlet -Parameters $Parameters))
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.message | Should -Match $Pattern
    }
}

Describe 'the fake module and the machine''s real one (task 050)' {
    BeforeAll {
        $script:Manifest = Import-PowerShellDataFile (Join-Path $script:FakeModules 'ExchangeOnlineManagement.psd1')
        $script:AllowList = @((Invoke-Sidecar -Lines @('{"aegis":1,"id":"1","type":"hello"}')).Lines[0].data.allowedCmdlets)
        # The two cmdlets the sidecar calls itself, outside its allow-list.
        $script:SessionCmdlets = @('Connect-ExchangeOnline', 'Disconnect-ExchangeOnline')
    }

    It 'exports every cmdlet of the allow-list from the fake module''s manifest, and no other' {
        $exports = @($script:Manifest.FunctionsToExport)
        $missing = @($script:AllowList | Where-Object { $exports -notcontains $_ })
        $extra = @($exports | Where-Object { $script:AllowList -notcontains $_ -and $script:SessionCmdlets -notcontains $_ })
        $missing -join ', ' | Should -BeNullOrEmpty -Because 'tests/sidecar/modules/ExchangeOnlineManagement/ExchangeOnlineManagement.psd1 must export every cmdlet of the allow-list (FunctionsToExport), else a test would reach for the machine''s real module; add the missing ones'
        $extra -join ', ' | Should -BeNullOrEmpty -Because 'the fake module exports only the allow-list and the connect and disconnect cmdlets'
    }

    It 'never imports a cmdlet the fake module lacks from the machine''s real module' {
        # A copy of the fake module without one export: the sidecar must fail on that cmdlet, not auto-load it from
        # an ExchangeOnlineManagement elsewhere on PSModulePath. The copy's version differs from any real one, as it
        # did on 2026-10-30: a loaded module of the same name and version shadows the real one, another version does
        # not, and auto-loading imports the real module beside it.
        $root = Join-Path $TestDrive 'modules'
        $copy = Join-Path $root 'ExchangeOnlineManagement'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        Copy-Item -Path $script:FakeModules -Destination $root -Recurse
        $manifestPath = Join-Path $copy 'ExchangeOnlineManagement.psd1'
        $text = (Get-Content -Raw $manifestPath) -replace ", 'Get-EXOMailboxFolderStatistics'", '' -replace "ModuleVersion = '[^']+'", "ModuleVersion = '0.0.50'"
        Set-Content -Path $manifestPath -Value $text -NoNewline
        (Import-PowerShellDataFile $manifestPath).FunctionsToExport | Should -Not -Contain 'Get-EXOMailboxFolderStatistics'

        $invoke = Get-InvokeLine -Cmdlet 'Get-EXOMailboxFolderStatistics' -Parameters @{ Identity = 'finance'; FolderScope = 'Calendar' }
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke) -ModuleRoot $root
        $run.Lines[0].type | Should -Be 'connected'
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[1].data.message | Should -Match "Get-EXOMailboxFolderStatistics' is not recognized"
    }
}
