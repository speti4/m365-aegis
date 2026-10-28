# The fake module behind tests/sidecar: the real cmdlet names, made-up answers, no network (CONTRIBUTING.md,
# "Tenant data never enters the repo").
$script:FakeConnected = $false

function Connect-ExchangeOnline {
    [CmdletBinding()]
    param(
        [string] $AccessToken,
        [string] $Organization,
        [switch] $ShowBanner,
        [switch] $SkipLoadingFormatData,
        [string[]] $CommandName
    )
    if (-not $AccessToken) { throw 'fake: an access token is required' }
    if ($Organization -ne 'contoso.onmicrosoft.com') { throw "fake: unknown organization '$Organization'" }
    # The real module prints a banner and warnings to the host; the sidecar must survive that noise.
    Write-Host 'fake ExchangeOnlineManagement: connected (this line must never reach stdout as a protocol line)'
    $script:FakeConnected = $true
}

function Disconnect-ExchangeOnline {
    [CmdletBinding()]
    param([switch] $Confirm)
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
    if ($Identity) { return $all | Where-Object { $_.Identity -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity } }
    return $all
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
