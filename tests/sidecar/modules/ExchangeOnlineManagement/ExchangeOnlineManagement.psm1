# The fake module behind tests/sidecar and the Core's sidecar tests: the real cmdlet names and parameter shapes,
# made-up answers for contoso.onmicrosoft.com, no network (CONTRIBUTING.md, "Tenant data never enters the repo").
# The parameters mirror the real cmdlets' so the sidecar calls them the way it calls the real ones; the ones the
# fake does not read are suppressed below on purpose. The data (tests/sidecar/README.md has the table):
#
#   alice      UserMailbox     alice@contoso.com      no forwarding
#   bob        UserMailbox     bob@contoso.com        forwards to bob@fabrikam.com (external), keeps a copy
#   carol      UserMailbox     carol@contoso.com      forwards to the finance mailbox (internal)
#   dave       UserMailbox     dave@contoso.com       forwards to the mail contact "Dave Private" (dave@example.com, external)
#   finance    SharedMailbox   finance@contoso.com    FullAccess alice, Send As alice, Send on Behalf bob
#   reception  SharedMailbox   reception@contoso.com  FullAccess alice and bob, Send As bob
#
# Identities that fail on purpose: 'broken' (a terminating error), 'nobody' (not found, a non-terminating error),
# 'busy' (throttled).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'the real cmdlets'' signatures')]
param()

$script:FakeConnected = $false

function ConvertTo-FakeMailbox {
    param([string] $Name, [string] $DisplayName, [string] $Type, [int] $Number, [string] $ForwardingSmtpAddress, [string] $ForwardingAddress,
        [bool] $KeepCopy, [string[]] $SendOnBehalf, [long] $Bytes, [long] $Items)
    [pscustomobject]@{
        Name = $Name
        Alias = $Name
        DisplayName = $DisplayName
        Identity = $Name
        UserPrincipalName = "$Name@contoso.com"
        PrimarySmtpAddress = "$Name@contoso.com"
        RecipientTypeDetails = $Type
        ExternalDirectoryObjectId = ('00000000-0000-0000-0000-0000000000{0:x2}' -f (0x10 + $Number))
        ExchangeGuid = ('00000000-0000-0000-0000-0000000000{0:x2}' -f (0x40 + $Number))
        DistinguishedName = "CN=$Name,OU=contoso.onmicrosoft.com,OU=Microsoft Exchange Hosted Organizations,DC=EURPR01A001,DC=PROD,DC=OUTLOOK,DC=COM"
        ForwardingSmtpAddress = if ($ForwardingSmtpAddress) { "smtp:$ForwardingSmtpAddress" } else { $null }
        ForwardingAddress = if ($ForwardingAddress) { $ForwardingAddress } else { $null }
        DeliverToMailboxAndForward = $KeepCopy
        GrantSendOnBehalfTo = [System.Collections.ArrayList]@($SendOnBehalf | Where-Object { $_ })
        FakeBytes = $Bytes
        FakeItems = $Items
    }
}

$script:Mailboxes = @(
    ConvertTo-FakeMailbox -Name 'alice' -DisplayName 'Alice Example' -Type 'UserMailbox' -Number 1 -Bytes 1288490188 -Items 5120
    ConvertTo-FakeMailbox -Name 'bob' -DisplayName 'Bob Example' -Type 'UserMailbox' -Number 2 -ForwardingSmtpAddress 'bob@fabrikam.com' -KeepCopy $true -Bytes 524288000 -Items 2048
    ConvertTo-FakeMailbox -Name 'carol' -DisplayName 'Carol Example' -Type 'UserMailbox' -Number 3 -ForwardingAddress 'Finance' -Bytes 10485760 -Items 300
    ConvertTo-FakeMailbox -Name 'dave' -DisplayName 'Dave Example' -Type 'UserMailbox' -Number 4 -ForwardingAddress 'Dave Private' -Bytes 0 -Items 0
    ConvertTo-FakeMailbox -Name 'finance' -DisplayName 'Finance' -Type 'SharedMailbox' -Number 5 -SendOnBehalf @('Bob Example') -Bytes 3221225472 -Items 40960
    ConvertTo-FakeMailbox -Name 'reception' -DisplayName 'Reception' -Type 'SharedMailbox' -Number 6 -Bytes 104857600 -Items 900
)

# FullAccess and Send As as the service lists them: SELF and inherited entries included, which the Core filters out.
$script:FullAccess = @(
    @{ Mailbox = 'alice'; User = 'NT AUTHORITY\SELF'; Inherited = $false; Deny = $false }
    @{ Mailbox = 'finance'; User = 'alice@contoso.com'; Inherited = $false; Deny = $false }
    @{ Mailbox = 'finance'; User = 'NT AUTHORITY\SYSTEM'; Inherited = $true; Deny = $false }
    @{ Mailbox = 'reception'; User = 'alice@contoso.com'; Inherited = $false; Deny = $false }
    @{ Mailbox = 'reception'; User = 'bob@contoso.com'; Inherited = $false; Deny = $false }
    @{ Mailbox = 'bob'; User = 'carol@contoso.com'; Inherited = $false; Deny = $true }
)
$script:SendAs = @(
    @{ Mailbox = 'alice'; Trustee = 'NT AUTHORITY\SELF' }
    @{ Mailbox = 'finance'; Trustee = 'alice@contoso.com' }
    @{ Mailbox = 'reception'; Trustee = 'bob@contoso.com' }
)
$script:Contacts = @(
    [pscustomobject]@{ Name = 'Dave Private'; Alias = 'dave-private'; DisplayName = 'Dave Private'; Identity = 'Dave Private'; PrimarySmtpAddress = 'dave@example.com'; RecipientTypeDetails = 'MailContact'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000030'; DistinguishedName = 'CN=Dave Private' }
)

function Assert-FakeConnected {
    if (-not $script:FakeConnected) { throw 'fake: not connected' }
}

function Assert-FakeIdentity {
    # The identities that fail on purpose; returns $false after a non-terminating error.
    param([string] $Identity)
    if ($Identity -eq 'broken') { throw 'fake: the service failed on purpose' }
    if ($Identity -eq 'busy') { throw 'fake: the request was throttled (Server Busy, MicroDelay); try again later' }
    return $true
}

function Find-FakeMailbox {
    [CmdletBinding()]
    param([string] $Identity)
    $found = @($script:Mailboxes | Where-Object {
            $_.Name -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity -or $_.UserPrincipalName -eq $Identity -or
            $_.ExternalDirectoryObjectId -eq $Identity -or $_.DisplayName -eq $Identity -or $_.ExchangeGuid -eq $Identity })
    if ($found.Count -eq 0) {
        Write-Error -Message "fake: The operation couldn't be performed because object '$Identity' couldn't be found on 'contoso.onmicrosoft.com'." -Category ObjectNotFound -ErrorId 'ManagementObjectNotFoundException'
        return
    }
    $found[0]
}

function Connect-ExchangeOnline {
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'the real module writes a banner to the host; the sidecar test asserts it never reaches the protocol channel')]
    param(
        [string] $AccessToken,
        [string] $Organization,
        [string] $UserPrincipalName,
        [switch] $ShowBanner,
        [switch] $SkipLoadingFormatData,
        [string[]] $CommandName
    )
    if (-not $AccessToken) { throw 'fake: an access token is required' }
    if ($AccessToken -eq 'expired-token') { throw 'fake: UnAuthorized. The access token has expired (401).' }
    if ($Organization -eq 'refuse.contoso.onmicrosoft.com') { throw "fake: the service refused the token $AccessToken (401)" }
    if ($Organization -and $Organization -ne 'contoso.onmicrosoft.com') { throw "fake: unknown organization '$Organization'" }
    if ($UserPrincipalName -and $UserPrincipalName -notlike '*@contoso.com') { throw "fake: unknown user '$UserPrincipalName'" }
    Write-Host 'fake ExchangeOnlineManagement: connected (this line must never reach stdout as a protocol line)'
    # The variant the connect asked for, on the console (stderr in the sidecar), so a test can see what reached the cmdlet.
    [Console]::Out.WriteLine("fake connect: SkipLoadingFormatData=$($SkipLoadingFormatData.IsPresent) CommandName=$($CommandName -join ',')")
    [Console]::Out.WriteLine('fake ExchangeOnlineManagement: a stray console line')
    $script:FakeConnected = $true
}

function Disconnect-ExchangeOnline {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    if ($PSCmdlet.ShouldProcess('the session', 'disconnect')) { $script:FakeConnected = $false }
}

function Get-EXOMailbox {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline)] [string] $Identity,
        [object] $ResultSize = 1000,
        [string[]] $RecipientTypeDetails,
        [string[]] $Properties,
        [string[]] $PropertySets,
        [string] $Filter
    )
    process {
        Assert-FakeConnected
        if ($Identity) {
            if (Assert-FakeIdentity $Identity) { Find-FakeMailbox -Identity $Identity }
            return
        }
        $all = $script:Mailboxes
        if ($RecipientTypeDetails) { $all = @($all | Where-Object { $RecipientTypeDetails -contains $_.RecipientTypeDetails }) }
        if ("$ResultSize" -ne 'Unlimited') { $all = @($all | Select-Object -First ([int] $ResultSize)) }
        $all
    }
}

function Get-EXORecipient {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [object] $ResultSize = 1000, [string[]] $Properties)
    process {
        Assert-FakeConnected
        if (-not (Assert-FakeIdentity $Identity)) { return }
        $contact = @($script:Contacts | Where-Object { $_.Name -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity })
        if ($contact.Count -gt 0) { return $contact[0] }
        Find-FakeMailbox -Identity $Identity
    }
}

function Get-EXOMailboxPermission {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [string] $User, [object] $ResultSize = 1000)
    process {
        Assert-FakeConnected
        if (-not (Assert-FakeIdentity $Identity)) { return }
        $mailbox = Find-FakeMailbox -Identity $Identity
        if (-not $mailbox) { return }
        foreach ($entry in $script:FullAccess | Where-Object { $_.Mailbox -eq $mailbox.Name }) {
            if ($User -and $entry.User -ne $User) { continue }
            [pscustomobject]@{
                Identity = $mailbox.Name
                User = $entry.User
                AccessRights = [System.Collections.ArrayList]@('FullAccess', 'ReadPermission')
                IsInherited = $entry.Inherited
                Deny = $entry.Deny
            }
        }
    }
}

function Get-EXORecipientPermission {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [string] $Trustee, [string[]] $AccessRights, [object] $ResultSize = 1000)
    process {
        Assert-FakeConnected
        $entries = $script:SendAs
        if ($Identity) {
            if (-not (Assert-FakeIdentity $Identity)) { return }
            $mailbox = Find-FakeMailbox -Identity $Identity
            if (-not $mailbox) { return }
            $entries = @($entries | Where-Object { $_.Mailbox -eq $mailbox.Name })
        }
        foreach ($entry in $entries) {
            if ($Trustee -and $entry.Trustee -ne $Trustee) { continue }
            [pscustomobject]@{
                Identity = $entry.Mailbox
                Trustee = $entry.Trustee
                AccessControlType = 'Allow'
                AccessRights = [System.Collections.ArrayList]@('SendAs')
                IsInherited = $false
            }
        }
    }
}

function Get-EXOMailboxStatistics {
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'the real cmdlet''s name')]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [string[]] $Properties, [string[]] $PropertySets)
    process {
        Assert-FakeConnected
        if (-not (Assert-FakeIdentity $Identity)) { return }
        $mailbox = Find-FakeMailbox -Identity $Identity
        if (-not $mailbox) { return }
        # The REST cmdlets return the sizes as the text of a ByteQuantifiedSize.
        $size = [string]::Format([cultureinfo]::InvariantCulture, '{0:N1} MB ({1:N0} bytes)', $mailbox.FakeBytes / 1MB, $mailbox.FakeBytes)
        [pscustomobject]@{
            DisplayName = $mailbox.DisplayName
            MailboxGuid = $mailbox.ExchangeGuid
            ItemCount = $mailbox.FakeItems
            TotalItemSize = $size
            DeletedItemCount = 3
            TotalDeletedItemSize = '1.5 KB (1,536 bytes)'
        }
    }
}

function Get-AcceptedDomain {
    [CmdletBinding()]
    param()
    Assert-FakeConnected
    [pscustomobject]@{ DomainName = 'contoso.com'; DomainType = 'Authoritative'; Default = $true }
    [pscustomobject]@{ DomainName = 'contoso.onmicrosoft.com'; DomainType = 'Authoritative'; Default = $false }
}
