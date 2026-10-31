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
#   reception  SharedMailbox   reception@contoso.com  FullAccess alice, bob and the group Finance Staff, Send As bob
#
# The mail-enabled security group Finance Staff (finance-staff@contoso.com, object id ...035) is a recipient only, whose
# members the Graph fixtures hold (task 044). Get-EXOMailboxPermission without an identity fails as the real one does
# (task 043 measured it).
#
# Identities that fail on purpose: 'broken' (a terminating error), 'nobody' (not found, a non-terminating error),
# 'busy' (throttled).
#
# The write cmdlets (task 053) change the made-up data of this process, so a read after a write sees it, and append
# each call as one JSON line ({"cmdlet","parameters"}) to the file M365AEGIS_FAKE_EXO_WRITES names, when it is set, so
# a test can assert what the apply sent. A mailbox right for the mail contact Dave Private fails as the service's
# does (it is no security principal), a non-terminating error; removing a right nobody holds fails the same way.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'the real cmdlets'' signatures')]
param()

$script:FakeConnected = $false
# Set by a connect with the token 'disconnect-fails-token': the disconnect at the sidecar's exit then fails (task 070).
$script:FakeDisconnectFails = $false

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
    @{ Mailbox = 'reception'; User = 'Finance Staff'; Inherited = $false; Deny = $false }
    @{ Mailbox = 'bob'; User = 'carol@contoso.com'; Inherited = $false; Deny = $true }
)
$script:SendAs = @(
    @{ Mailbox = 'alice'; Trustee = 'NT AUTHORITY\SELF' }
    @{ Mailbox = 'finance'; Trustee = 'alice@contoso.com' }
    @{ Mailbox = 'reception'; Trustee = 'bob@contoso.com' }
)
$script:Contacts = @(
    [pscustomobject]@{ Name = 'Dave Private'; Alias = 'dave-private'; DisplayName = 'Dave Private'; Identity = 'Dave Private'; PrimarySmtpAddress = 'dave@example.com'; RecipientTypeDetails = 'MailContact'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000030'; DistinguishedName = 'CN=Dave Private' }
    [pscustomobject]@{ Name = 'Finance Staff'; Alias = 'finance-staff'; DisplayName = 'Finance Staff'; Identity = 'Finance Staff'; PrimarySmtpAddress = 'finance-staff@contoso.com'; RecipientTypeDetails = 'MailUniversalSecurityGroup'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000035'; DistinguishedName = 'CN=Finance Staff' }
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
    $script:FakeDisconnectFails = $AccessToken -eq 'disconnect-fails-token'
}

function Disconnect-ExchangeOnline {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    if ($script:FakeDisconnectFails) { throw 'fake: the disconnect failed on purpose' }
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
        $contact = @($script:Contacts | Where-Object { $_.Name -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity -or $_.ExternalDirectoryObjectId -eq $Identity })
        if ($contact.Count -gt 0) { return $contact[0] }
        Find-FakeMailbox -Identity $Identity
    }
}

function Get-EXOMailboxPermission {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [string] $User, [object] $ResultSize = 1000)
    process {
        Assert-FakeConnected
        # The real cmdlet refuses to run without an identity, whatever its documented default parameter set says.
        if (-not $Identity) {
            throw 'Identity is a mandatory value to provide for running Get-ExoMailboxPermission. You can specify identity by using any one of the three available parameters: Identity, ExternalDirectoryObjectId, UserPrincipalName.'
        }
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

function Get-EXOMailboxFolderStatistics {
    # The calendar folders of a mailbox: the default one (FolderType Calendar) is named in the mailbox's language, so
    # Reception's is 'Kalender' (task 042); Finance also has a calendar of its own under the default one.
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'the real cmdlet''s name')]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [string] $FolderScope, [string[]] $Properties)
    process {
        Assert-FakeConnected
        if (-not (Assert-FakeIdentity $Identity)) { return }
        $mailbox = Find-FakeMailbox -Identity $Identity
        if (-not $mailbox) { return }
        $calendar = if ($mailbox.Name -eq 'reception') { 'Kalender' } else { 'Calendar' }
        $folders = @(
            [pscustomobject]@{ Name = 'Inbox'; FolderPath = '/Inbox'; FolderType = 'Inbox' }
            [pscustomobject]@{ Name = $calendar; FolderPath = "/$calendar"; FolderType = 'Calendar' }
        )
        if ($mailbox.Name -eq 'finance') {
            $folders += [pscustomobject]@{ Name = 'Holidays'; FolderPath = '/Calendar/Holidays'; FolderType = 'User Created' }
        }
        if ($FolderScope -eq 'Calendar') { $folders = @($folders | Where-Object { $_.FolderType -ne 'Inbox' }) }
        # Each folder names its mailbox as the service's folder statistics do (task 100): the identity, the mailbox's name
        # before the folder's path, and the mailbox's GUID, so a piped read over many mailboxes can tell them apart.
        foreach ($folder in $folders) {
            $folder | Add-Member -NotePropertyName Identity -NotePropertyValue ($mailbox.Name + '\' + $folder.FolderPath.Trim('/').Replace('/', '\'))
            $folder | Add-Member -NotePropertyName ContentMailboxGuid -NotePropertyValue $mailbox.ExchangeGuid
        }
        $folders
    }
}

function Get-AcceptedDomain {
    [CmdletBinding()]
    param()
    Assert-FakeConnected
    [pscustomobject]@{ DomainName = 'contoso.com'; DomainType = 'Authoritative'; Default = $true }
    [pscustomobject]@{ DomainName = 'contoso.onmicrosoft.com'; DomainType = 'Authoritative'; Default = $false }
}

function Write-FakeWrite {
    # One write as the test sees it: a JSON line in the file M365AEGIS_FAKE_EXO_WRITES names, and a console line.
    param([string] $Cmdlet, [System.Collections.IDictionary] $Parameters)
    $common = [System.Management.Automation.PSCmdlet]::CommonParameters + [System.Management.Automation.PSCmdlet]::OptionalCommonParameters
    $kept = [ordered]@{}
    foreach ($key in @($Parameters.Keys | Sort-Object)) {
        if ($common -contains $key) { continue }
        $kept[$key] = $Parameters[$key]
    }
    $line = [ordered]@{ cmdlet = $Cmdlet; parameters = $kept } | ConvertTo-Json -Compress -Depth 4
    [Console]::Out.WriteLine("fake write: $line")
    if ($env:M365AEGIS_FAKE_EXO_WRITES) { Add-Content -LiteralPath $env:M365AEGIS_FAKE_EXO_WRITES -Value $line -Encoding utf8 }
}

function Find-FakePrincipal {
    # The recipient a right is granted to: a mailbox or the group Finance Staff; the mail contact is no security
    # principal, as the service says. Returns $null after a non-terminating error.
    [CmdletBinding()]
    param([string] $Identity)
    $recipient = Get-EXORecipient -Identity $Identity -ErrorAction SilentlyContinue
    if (-not $recipient) {
        Write-Error -Message "fake: The operation couldn't be performed because object '$Identity' couldn't be found on 'contoso.onmicrosoft.com'." -Category ObjectNotFound -ErrorId 'ManagementObjectNotFoundException'
        return
    }
    if ($recipient.RecipientTypeDetails -eq 'MailContact') {
        Write-Error -Message "fake: The user '$($recipient.DisplayName)' isn't a valid security principal for a mailbox permission." -Category InvalidArgument -ErrorId 'InvalidTrusteeException'
        return
    }
    $recipient
}

function Get-FakePrincipalName {
    # How a permission entry names its holder: a mailbox by its address, a group by its name (as the service lists them).
    param($Principal)
    if ($Principal.RecipientTypeDetails -like '*Group') { return $Principal.Name }
    return $Principal.PrimarySmtpAddress
}

function Add-MailboxPermission {
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $Identity, [string] $User, [string[]] $AccessRights, [string] $InheritanceType, [bool] $AutoMapping)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $mailbox = Find-FakeMailbox -Identity $Identity
    if (-not $mailbox) { return }
    $principal = Find-FakePrincipal -Identity $User
    if (-not $principal) { return }
    if (-not $PSCmdlet.ShouldProcess($mailbox.Name, 'Add-MailboxPermission')) { return }
    Write-FakeWrite 'Add-MailboxPermission' $PSBoundParameters
    $holder = Get-FakePrincipalName $principal
    if (-not ($script:FullAccess | Where-Object { $_.Mailbox -eq $mailbox.Name -and $_.User -eq $holder -and -not $_.Deny })) {
        $script:FullAccess += @{ Mailbox = $mailbox.Name; User = $holder; Inherited = $false; Deny = $false }
    }
    [pscustomobject]@{ Identity = $mailbox.Name; User = $holder; AccessRights = [System.Collections.ArrayList]@('FullAccess'); IsInherited = $false; Deny = $false }
}

function Remove-MailboxPermission {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([string] $Identity, [string] $User, [string[]] $AccessRights, [string] $InheritanceType)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $mailbox = Find-FakeMailbox -Identity $Identity
    if (-not $mailbox) { return }
    $principal = Find-FakePrincipal -Identity $User
    if (-not $principal) { return }
    $holder = Get-FakePrincipalName $principal
    $held = @($script:FullAccess | Where-Object { $_.Mailbox -eq $mailbox.Name -and $_.User -eq $holder -and -not $_.Deny })
    if ($held.Count -eq 0) {
        Write-Error -Message "fake: Can't remove the access control entry on the object '$($mailbox.Name)' for account '$holder' because the ACE doesn't exist on the object." -Category InvalidOperation -ErrorId 'ACENotFoundException'
        return
    }
    if (-not $PSCmdlet.ShouldProcess($mailbox.Name, 'Remove-MailboxPermission')) { return }
    Write-FakeWrite 'Remove-MailboxPermission' $PSBoundParameters
    $script:FullAccess = @($script:FullAccess | Where-Object { -not ($_.Mailbox -eq $mailbox.Name -and $_.User -eq $holder -and -not $_.Deny) })
}

function Add-RecipientPermission {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([string] $Identity, [string] $Trustee, [string[]] $AccessRights)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $mailbox = Find-FakeMailbox -Identity $Identity
    if (-not $mailbox) { return }
    $principal = Find-FakePrincipal -Identity $Trustee
    if (-not $principal) { return }
    if (-not $PSCmdlet.ShouldProcess($mailbox.Name, 'Add-RecipientPermission')) { return }
    Write-FakeWrite 'Add-RecipientPermission' $PSBoundParameters
    $holder = Get-FakePrincipalName $principal
    if (-not ($script:SendAs | Where-Object { $_.Mailbox -eq $mailbox.Name -and $_.Trustee -eq $holder })) {
        $script:SendAs += @{ Mailbox = $mailbox.Name; Trustee = $holder }
    }
    [pscustomobject]@{ Identity = $mailbox.Name; Trustee = $holder; AccessControlType = 'Allow'; AccessRights = [System.Collections.ArrayList]@('SendAs'); IsInherited = $false }
}

function Remove-RecipientPermission {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([string] $Identity, [string] $Trustee, [string[]] $AccessRights)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $mailbox = Find-FakeMailbox -Identity $Identity
    if (-not $mailbox) { return }
    $principal = Find-FakePrincipal -Identity $Trustee
    if (-not $principal) { return }
    $holder = Get-FakePrincipalName $principal
    if (-not ($script:SendAs | Where-Object { $_.Mailbox -eq $mailbox.Name -and $_.Trustee -eq $holder })) {
        Write-Error -Message "fake: Can't remove the access control entry on the object '$($mailbox.Name)' for account '$holder' because the ACE doesn't exist on the object." -Category InvalidOperation -ErrorId 'ACENotFoundException'
        return
    }
    if (-not $PSCmdlet.ShouldProcess($mailbox.Name, 'Remove-RecipientPermission')) { return }
    Write-FakeWrite 'Remove-RecipientPermission' $PSBoundParameters
    $script:SendAs = @($script:SendAs | Where-Object { -not ($_.Mailbox -eq $mailbox.Name -and $_.Trustee -eq $holder) })
}

function Set-Mailbox {
    # Send on Behalf as the multi-valued property's change (@{Add=...} / @{Remove=...}, stored as display names, as the
    # service lists them), and the forwarding.
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $Identity, [object] $GrantSendOnBehalfTo, [object] $ForwardingSmtpAddress, [object] $ForwardingAddress, [object] $DeliverToMailboxAndForward)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $mailbox = Find-FakeMailbox -Identity $Identity
    if (-not $mailbox) { return }
    $delegates = [System.Collections.Generic.List[object]]::new()
    if ($PSBoundParameters.ContainsKey('GrantSendOnBehalfTo')) {
        if ($GrantSendOnBehalfTo -isnot [hashtable]) { throw 'fake: GrantSendOnBehalfTo takes @{Add=...} or @{Remove=...} here' }
        foreach ($operation in @('Add', 'Remove')) {
            foreach ($name in @($GrantSendOnBehalfTo[$operation] | Where-Object { $_ })) {
                $principal = Find-FakePrincipal -Identity $name
                if (-not $principal) { return }
                $delegates.Add([pscustomobject]@{ Operation = $operation; Name = $principal.DisplayName })
            }
        }
    }
    if (-not $PSCmdlet.ShouldProcess($mailbox.Name, 'Set-Mailbox')) { return }
    Write-FakeWrite 'Set-Mailbox' $PSBoundParameters
    foreach ($delegate in $delegates) {
        if ($delegate.Operation -eq 'Add' -and $mailbox.GrantSendOnBehalfTo -notcontains $delegate.Name) { [void] $mailbox.GrantSendOnBehalfTo.Add($delegate.Name) }
        if ($delegate.Operation -eq 'Remove') { $mailbox.GrantSendOnBehalfTo.Remove($delegate.Name) }
    }
    if ($PSBoundParameters.ContainsKey('ForwardingSmtpAddress')) {
        $mailbox.ForwardingSmtpAddress = if ($ForwardingSmtpAddress) { 'smtp:' + ([string] $ForwardingSmtpAddress -replace '^smtp:', '') } else { $null }
    }
    if ($PSBoundParameters.ContainsKey('ForwardingAddress')) { $mailbox.ForwardingAddress = if ($ForwardingAddress) { [string] $ForwardingAddress } else { $null } }
    if ($PSBoundParameters.ContainsKey('DeliverToMailboxAndForward')) { $mailbox.DeliverToMailboxAndForward = [bool] $DeliverToMailboxAndForward }
}

# The member writes of a distribution or mail-enabled security group (task 061): the distribution group Sales
# (sales@contoso.com, object id ...036) and the mail-enabled security group Finance Staff (finance-staff@contoso.com,
# ...035), their members by address, as the service names a member by its recipient. A member is any recipient
# Get-EXORecipient knows; adding one who is a member already, or removing one who is not, fails as the service's does
# (a non-terminating error), and the failing identities 'broken', 'nobody' and 'busy' fail the writes too.
#
# The owners (task 064) are ManagedBy, kept as the service lists them, by each recipient's name: Sales is managed by
# alice, Finance Staff by carol and by 'Organization Management', a role group no recipient lookup knows. The two groups
# the Graph fixtures list without an owner (task 089) are here too: the distribution group All Staff
# (allstaff@contoso.com, ...03b) with an empty ManagedBy, and the mail-enabled security group Sales Managers
# (salesmanagers@contoso.com, ...03d) managed by alice.
$script:Groups = @(
    [pscustomobject]@{ Name = 'Sales'; Alias = 'sales'; DisplayName = 'Sales'; PrimarySmtpAddress = 'sales@contoso.com'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000036'
        RecipientTypeDetails = 'MailUniversalDistributionGroup'
        Members = [System.Collections.Generic.List[string]]@('bob@contoso.com', 'dave@example.com', 'alice@contoso.com')
        ManagedBy = [System.Collections.Generic.List[string]]@('alice') }
    [pscustomobject]@{ Name = 'Finance Staff'; Alias = 'finance-staff'; DisplayName = 'Finance Staff'; PrimarySmtpAddress = 'finance-staff@contoso.com'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-000000000035'
        RecipientTypeDetails = 'MailUniversalSecurityGroup'
        Members = [System.Collections.Generic.List[string]]@('carol@contoso.com')
        ManagedBy = [System.Collections.Generic.List[string]]@('carol', 'Organization Management') }
    [pscustomobject]@{ Name = 'All Staff'; Alias = 'allstaff'; DisplayName = 'All Staff'; PrimarySmtpAddress = 'allstaff@contoso.com'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-00000000003b'
        RecipientTypeDetails = 'MailUniversalDistributionGroup'
        Members = [System.Collections.Generic.List[string]]@('alice@contoso.com', 'bob@contoso.com')
        ManagedBy = [System.Collections.Generic.List[string]]@() }
    [pscustomobject]@{ Name = 'Sales Managers'; Alias = 'salesmanagers'; DisplayName = 'Sales Managers'; PrimarySmtpAddress = 'salesmanagers@contoso.com'; ExternalDirectoryObjectId = '00000000-0000-0000-0000-00000000003d'
        RecipientTypeDetails = 'MailUniversalSecurityGroup'
        Members = [System.Collections.Generic.List[string]]@('alice@contoso.com')
        ManagedBy = [System.Collections.Generic.List[string]]@('alice') }
)

function Find-FakeGroup {
    [CmdletBinding()]
    param([string] $Identity)
    $found = @($script:Groups | Where-Object {
            $_.Name -eq $Identity -or $_.Alias -eq $Identity -or $_.PrimarySmtpAddress -eq $Identity -or $_.ExternalDirectoryObjectId -eq $Identity })
    if ($found.Count -eq 0) {
        Write-Error -Message "fake: The operation couldn't be performed because object '$Identity' couldn't be found on 'contoso.onmicrosoft.com'." -Category ObjectNotFound -ErrorId 'ManagementObjectNotFoundException'
        return
    }
    $found[0]
}

function Find-FakeMember {
    # The recipient a member cmdlet names, by its address; $null after a non-terminating error.
    [CmdletBinding()]
    param([string] $Identity)
    if (-not (Assert-FakeIdentity $Identity)) { return }
    Get-EXORecipient -Identity $Identity
}

function Add-DistributionGroupMember {
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $Identity, [string] $Member, [switch] $BypassSecurityGroupManagerCheck)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $group = Find-FakeGroup -Identity $Identity
    if (-not $group) { return }
    $recipient = Find-FakeMember -Identity $Member
    if (-not $recipient) { return }
    if ($group.Members -contains $recipient.PrimarySmtpAddress) {
        Write-Error -Message "fake: The recipient ""$($recipient.PrimarySmtpAddress)"" is already a member of the group ""$($group.Name)""." -Category InvalidArgument -ErrorId 'MemberAlreadyExistsException'
        return
    }
    if (-not $PSCmdlet.ShouldProcess($group.Name, 'Add-DistributionGroupMember')) { return }
    Write-FakeWrite 'Add-DistributionGroupMember' $PSBoundParameters
    $group.Members.Add($recipient.PrimarySmtpAddress)
}

function Remove-DistributionGroupMember {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([string] $Identity, [string] $Member, [switch] $BypassSecurityGroupManagerCheck)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $group = Find-FakeGroup -Identity $Identity
    if (-not $group) { return }
    $recipient = Find-FakeMember -Identity $Member
    if (-not $recipient) { return }
    if ($group.Members -notcontains $recipient.PrimarySmtpAddress) {
        Write-Error -Message "fake: The recipient ""$($recipient.PrimarySmtpAddress)"" isn't a member of the group ""$($group.Name)""." -Category InvalidArgument -ErrorId 'MemberNotFoundException'
        return
    }
    if (-not $PSCmdlet.ShouldProcess($group.Name, 'Remove-DistributionGroupMember')) { return }
    Write-FakeWrite 'Remove-DistributionGroupMember' $PSBoundParameters
    [void] $group.Members.Remove($recipient.PrimarySmtpAddress)
}

function ConvertTo-FakeGroup {
    # A group as Get-DistributionGroup returns it.
    param([object] $Group)
    [pscustomobject]@{
        Name = $Group.Name
        Alias = $Group.Alias
        DisplayName = $Group.DisplayName
        Identity = $Group.Name
        PrimarySmtpAddress = $Group.PrimarySmtpAddress
        ExternalDirectoryObjectId = $Group.ExternalDirectoryObjectId
        RecipientTypeDetails = $Group.RecipientTypeDetails
        ManagedBy = [System.Collections.ArrayList]@($Group.ManagedBy)
    }
}

function Get-DistributionGroup {
    # A distribution or mail-enabled security group with its owners (task 064): ManagedBy lists them by name. Without an
    # identity, every group up to ResultSize, as the bulk read of owners-missing --all-types asks (task 089).
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)] [string] $Identity, [object] $ResultSize = 1000)
    process {
        Assert-FakeConnected
        if (-not $Identity) {
            $groups = @($script:Groups)
            if ("$ResultSize" -ne 'Unlimited') { $groups = @($groups | Select-Object -First ([int] $ResultSize)) }
            foreach ($group in $groups) { ConvertTo-FakeGroup $group }
            return
        }
        if (-not (Assert-FakeIdentity $Identity)) { return }
        $group = Find-FakeGroup -Identity $Identity
        if (-not $group) { return }
        ConvertTo-FakeGroup $group
    }
}

function Set-DistributionGroup {
    # The owners only (task 064): -ManagedBy as the multi-valued property's change (@{Add=...} / @{Remove=...}), each
    # owner a recipient Get-EXORecipient knows, kept by its name as the service lists it. Removing an owner who is not
    # one fails as the service's does (a non-terminating error); adding one who is changes nothing.
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $Identity, [object] $ManagedBy, [switch] $BypassSecurityGroupManagerCheck)
    Assert-FakeConnected
    if (-not (Assert-FakeIdentity $Identity)) { return }
    $group = Find-FakeGroup -Identity $Identity
    if (-not $group) { return }
    if ($ManagedBy -isnot [hashtable]) { throw 'fake: ManagedBy takes @{Add=...} or @{Remove=...} here' }
    $changes = [System.Collections.Generic.List[object]]::new()
    foreach ($operation in @('Add', 'Remove')) {
        foreach ($name in @($ManagedBy[$operation] | Where-Object { $_ })) {
            $recipient = Find-FakeMember -Identity $name
            if (-not $recipient) { return }
            if ($operation -eq 'Remove' -and $group.ManagedBy -notcontains $recipient.Name) {
                Write-Error -Message "fake: The recipient ""$($recipient.PrimarySmtpAddress)"" isn't an owner of the group ""$($group.Name)""." -Category InvalidArgument -ErrorId 'ManagedByNotFoundException'
                return
            }
            $changes.Add([pscustomobject]@{ Operation = $operation; Name = $recipient.Name })
        }
    }
    if (-not $PSCmdlet.ShouldProcess($group.Name, 'Set-DistributionGroup')) { return }
    Write-FakeWrite 'Set-DistributionGroup' $PSBoundParameters
    foreach ($change in $changes) {
        if ($change.Operation -eq 'Add' -and $group.ManagedBy -notcontains $change.Name) { $group.ManagedBy.Add($change.Name) }
        if ($change.Operation -eq 'Remove') { [void] $group.ManagedBy.Remove($change.Name) }
    }
}
