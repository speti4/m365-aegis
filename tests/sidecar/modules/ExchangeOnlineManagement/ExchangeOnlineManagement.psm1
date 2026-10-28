# The fake module behind tests/sidecar: the real cmdlet names and parameter shapes, made-up answers, no network
# (CONTRIBUTING.md, "Tenant data never enters the repo"). The parameters mirror the real cmdlets' so the sidecar
# can call them the way it calls the real ones; the ones the fake does not read are suppressed below on purpose.
$script:FakeConnected = $false

function Connect-ExchangeOnline {
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'the real cmdlet''s signature')]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'the real module writes a banner to the host; the sidecar test asserts it never reaches the protocol channel')]
    param(
        [string] $AccessToken,
        [string] $Organization,
        [switch] $ShowBanner,
        [switch] $SkipLoadingFormatData,
        [string[]] $CommandName
    )
    if (-not $AccessToken) { throw 'fake: an access token is required' }
    if ($Organization -ne 'contoso.onmicrosoft.com') { throw "fake: unknown organization '$Organization'" }
    Write-Host 'fake ExchangeOnlineManagement: connected (this line must never reach stdout as a protocol line)'
    $script:FakeConnected = $true
}

function Disconnect-ExchangeOnline {
    [CmdletBinding()]
    param()
    $script:FakeConnected = $false
}

function Get-Mailbox {
    [CmdletBinding()]
    param([string] $Identity, [int] $ResultSize = 1000)
    if (-not $script:FakeConnected) { throw 'fake: not connected' }
    $all = @(
        [pscustomobject]@{ Identity = 'shared-finance'; PrimarySmtpAddress = 'finance@contoso.onmicrosoft.com'; RecipientTypeDetails = 'SharedMailbox'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000011' },
        [pscustomobject]@{ Identity = 'alice'; PrimarySmtpAddress = 'alice@contoso.onmicrosoft.com'; RecipientTypeDetails = 'UserMailbox'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000012' }
    )
    if ($Identity) { $all = @($all | Where-Object { $_.Identity -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity }) }
    return $all | Select-Object -First $ResultSize
}

function Get-EXOMailbox {
    [CmdletBinding()]
    param([string] $Identity, [int] $ResultSize = 1000)
    Get-Mailbox @PSBoundParameters
}

function Get-EXOMailboxPermission {
    [CmdletBinding()]
    param([string] $Identity)
    if (-not $script:FakeConnected) { throw 'fake: not connected' }
    [pscustomobject]@{ Identity = $Identity; User = 'alice@contoso.onmicrosoft.com'; AccessRights = @('FullAccess'); IsInherited = $false }
}
