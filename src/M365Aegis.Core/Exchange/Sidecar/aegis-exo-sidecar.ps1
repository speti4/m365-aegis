#Requires -Version 7.6
<#
.SYNOPSIS
The Exchange Online sidecar: a long-lived pwsh process the Core drives over JSON lines (D-004).

.DESCRIPTION
The Core (Exchange/Sidecar in M365Aegis.Core) starts this script with -NoProfile -NonInteractive and talks to it on
stdin and stdout, UTF-8, one JSON object per line, tagged {"aegis":1,...}. Everything the script prints that is not
a protocol line goes to stderr, so a module banner or a Write-Host cannot corrupt the channel. The messages:

  {"aegis":1,"id":"1","type":"hello"}
      -> {"type":"hello","data":{"pwsh","module","allowedCmdlets","moduleBase","modulePath"}}
  {"aegis":1,"id":"2","type":"connect","data":{"accessToken":"...","organization":"contoso.onmicrosoft.com"}}
      -> {"type":"connected","data":{"organization","userPrincipalName","module","importMs","connectMs"}} / error
      data.userPrincipalName may join or replace data.organization; the token is never written anywhere.
      data.skipLoadingFormatData and data.commandName (true when absent) pick the connect's variant, and
      data.checkCommands adds "missing", the allowed cmdlets the session does not answer (task 011's measurement).
  {"aegis":1,"id":"3","type":"reconnect","data":{...as connect...}}
      -> connected / error: the session is dropped and connected again with the fresh token
  {"aegis":1,"id":"4","type":"invoke","data":{"cmdlet":"Get-EXOMailbox","parameters":{...},"properties":[...],"input":[...]}}
      -> {"type":"result","data":{"items":[...],"count":N,"errors":[...]}} / error
      properties picks the fields of each object (all of them when omitted); input is piped into the cmdlet.
      Every value is plain: a string, a number, a boolean, null or a list of strings. errors lists the
      non-terminating errors of a run that still returned objects; a run that returned none fails instead.
  {"aegis":1,"type":"exit"}
      -> the session is disconnected and the process ends

The error replies carry {"code","message","errorId","category"}: bad-request, unknown-type, module-missing,
not-connected, cmdlet-not-allowed, connect-failed, cmdlet-failed. Only the cmdlets of $AllowedCmdlets run; the
C# allow-list (Exchange/Sidecar/ExoCmdlets.cs) is the one place a cmdlet is admitted and a test holds this list to
it. tests/sidecar/ runs this script against a fake module.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
$PSStyle.OutputRendering = 'PlainText'

# The protocol owns stdout, in UTF-8 whatever the console's code page. Everything else the process would print (a
# module banner, a Write-Host, a stray Out-Default) goes to stderr: the console's standard output is swapped for
# its error stream, and the protocol reads and writes the raw standard streams.
$script:Utf8 = [System.Text.UTF8Encoding]::new($false)
$script:ProtocolIn = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), $script:Utf8)
$script:ProtocolOut = [System.IO.StreamWriter]::new([Console]::OpenStandardOutput(), $script:Utf8)
[Console]::SetOut([Console]::Error)

# A test module root (M365AEGIS_EXO_MODULE_ROOT) turns pwsh's module auto-loading off (task 050): a cmdlet the fake
# module does not export would otherwise be found in the machine's real ExchangeOnlineManagement and imported from
# there. The two built-in modules the script itself uses are imported first, since nothing loads them on demand now.
if ($env:M365AEGIS_EXO_MODULE_ROOT) {
    Import-Module Microsoft.PowerShell.Management, Microsoft.PowerShell.Utility -ErrorAction Stop
    $PSModuleAutoLoadingPreference = 'None'
}

$script:ProtocolVersion = 1
# The cmdlets the sidecar runs, in the order of ExoCmdlets.Allowed in the Core; a test compares the two lists.
$script:AllowedCmdlets = @(
    'Get-EXOMailbox', 'Get-EXOMailboxPermission', 'Get-EXORecipientPermission', 'Get-EXOMailboxStatistics',
    'Get-EXORecipient', 'Get-AcceptedDomain', 'Get-EXOMailboxFolderStatistics'
)
$script:Connected = $false
# The tokens this process was handed, so an error message that quotes one is scrubbed before it is written.
$script:Secrets = [System.Collections.Generic.List[string]]::new()

function Find-ExoModule {
    # The newest installed ExchangeOnlineManagement, or null. M365AEGIS_EXO_MODULE_ROOT names one folder to look in
    # instead of PSModulePath (the tests point it at a fake module, or at nothing).
    $root = $env:M365AEGIS_EXO_MODULE_ROOT
    if ($root) {
        $manifest = Join-Path $root 'ExchangeOnlineManagement' 'ExchangeOnlineManagement.psd1'
        if (-not (Test-Path $manifest)) { return $null }
        return Get-Module -ListAvailable $manifest | Select-Object -First 1
    }
    return Get-Module -ListAvailable -Name ExchangeOnlineManagement | Sort-Object Version -Descending | Select-Object -First 1
}

function Hide-Secret {
    # A message with every token this process holds, and anything shaped like a JWT, replaced.
    param([string] $Text)
    if (-not $Text) { return $Text }
    foreach ($secret in $script:Secrets) { $Text = $Text.Replace($secret, '<token>') }
    return [regex]::Replace($Text, 'eyJ[A-Za-z0-9_-]{8,}(\.[A-Za-z0-9_-]*){0,2}', '<token>')
}

function Write-Protocol {
    param([System.Collections.IDictionary] $Message)
    $Message['aegis'] = $script:ProtocolVersion
    # One line, compact; the values are flat already (ConvertTo-PlainValue), so the depth only covers the envelope.
    $line = $Message | ConvertTo-Json -Compress -Depth 8
    $script:ProtocolOut.Write($line)
    $script:ProtocolOut.Write("`n")
    $script:ProtocolOut.Flush()
}

function Write-Reply {
    param([System.Collections.IDictionary] $Request, [string] $Type, $Data)
    $reply = [ordered]@{ type = $Type }
    if ($Request.Contains('id')) { $reply['id'] = $Request['id'] }
    if ($null -ne $Data) { $reply['data'] = $Data }
    Write-Protocol $reply
}

function Write-ErrorReply {
    param([System.Collections.IDictionary] $Request, [string] $Code, [string] $Text, $ErrorRecord)
    $data = [ordered]@{ code = $Code; message = (Hide-Secret $Text) }
    if ($ErrorRecord) {
        # A thrown string is its own error id, so the id is scrubbed like the message.
        $data['errorId'] = Hide-Secret ([string] $ErrorRecord.FullyQualifiedErrorId)
        $data['category'] = Hide-Secret ([string] $ErrorRecord.CategoryInfo.Reason)
    }
    Write-Reply -Request $Request -Type 'error' -Data $data
}

function ConvertTo-PlainValue {
    # A property value as the protocol carries it: a string, a number, a boolean, null, or a list of strings.
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value -is [bool]) { return $Value }
    if ($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('o', [cultureinfo]::InvariantCulture) }
    if ($Value -is [enum]) { return $Value.ToString() }
    if ($Value.GetType().IsPrimitive -or $Value -is [decimal]) { return $Value }
    if ($Value -is [System.Collections.IEnumerable]) {
        $list = [System.Collections.Generic.List[object]]::new()
        foreach ($element in $Value) {
            $list.Add($(if ($null -eq $element) { $null } else { [string] $element }))
        }
        return , $list.ToArray()
    }
    return $Value.ToString()
}

function ConvertTo-PlainItem {
    param($Object, [string[]] $Properties)
    $item = [ordered]@{}
    if ($null -eq $Object) { return $item }
    $names = if ($Properties) { $Properties } else { @($Object.PSObject.Properties | ForEach-Object Name) }
    foreach ($name in $names) {
        $property = $Object.PSObject.Properties[$name]
        $item[$name] = if ($null -ne $property) { ConvertTo-PlainValue $property.Value } else { $null }
    }
    return $item
}

function Invoke-Hello {
    param([System.Collections.IDictionary] $Request)
    $module = Find-ExoModule
    Write-Reply -Request $Request -Type 'hello' -Data ([ordered]@{
        pwsh = $PSVersionTable.PSVersion.ToString()
        module = if ($module) { $module.Version.ToString() } else { $null }
        allowedCmdlets = $script:AllowedCmdlets
        # Where the module and the module path are, for the connect measurement's OneDrive check (task 011).
        moduleBase = if ($module) { $module.ModuleBase } else { $null }
        modulePath = $env:PSModulePath
    })
}

function Invoke-Connect {
    param([System.Collections.IDictionary] $Request, [switch] $Again)
    $data = $Request['data']
    if ($data -isnot [System.Collections.IDictionary] -or -not $data['accessToken'] -or
        (-not $data['organization'] -and -not $data['userPrincipalName'])) {
        Write-ErrorReply $Request 'bad-request' 'connect needs data.accessToken and data.organization or data.userPrincipalName'
        return
    }
    $token = [string] $data['accessToken']
    if (-not $script:Secrets.Contains($token)) { $script:Secrets.Add($token) }
    # The import time covers finding the module on the module path too, where a OneDrive folder would slow it.
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $module = Find-ExoModule
    if ($null -eq $module) {
        Write-ErrorReply $Request 'module-missing' 'the ExchangeOnlineManagement module is not installed'
        return
    }

    # The information stream (Write-Host, the module banner) is dropped; a terminating error still throws.
    Import-Module $module.Path -ErrorAction Stop 6> $null | Out-Null
    $importMs = $watch.ElapsedMilliseconds
    if ($Again -and $script:Connected) {
        $script:Connected = $false
        Disconnect-ExchangeOnline -Confirm:$false 6> $null | Out-Null
    }

    # The default variant (task 011): no format data (the protocol never formats an object) and only the REST
    # cmdlets of the allow-list imported; the Get-EXO* cmdlets are the module's own and need no import. The
    # measurement switches either off with data.skipLoadingFormatData or data.commandName set to false.
    $connect = @{ AccessToken = $token; ShowBanner = $false }
    if ($data['skipLoadingFormatData'] -ne $false) { $connect['SkipLoadingFormatData'] = $true }
    $imported = @($script:AllowedCmdlets | Where-Object { $_ -notlike 'Get-EXO*' })
    if ($data['commandName'] -ne $false -and $imported.Count -gt 0) { $connect['CommandName'] = $imported }
    if ($data['organization']) { $connect['Organization'] = [string] $data['organization'] }
    if ($data['userPrincipalName']) { $connect['UserPrincipalName'] = [string] $data['userPrincipalName'] }
    $watch.Restart()
    try {
        Connect-ExchangeOnline @connect 6> $null | Out-Null
    } catch {
        Write-ErrorReply $Request 'connect-failed' $_.Exception.Message $_
        return
    }
    $script:Connected = $true
    $connectMs = $watch.ElapsedMilliseconds
    $reply = [ordered]@{
        organization = $data['organization']
        userPrincipalName = $data['userPrincipalName']
        module = $module.Version.ToString()
        importMs = $importMs
        connectMs = $connectMs
    }
    # data.checkCommands: which cmdlets of the allow-list the session does not answer (the measurement's check).
    if ($data['checkCommands'] -eq $true) {
        $reply['missing'] = @($script:AllowedCmdlets | Where-Object { -not (Get-Command $_ -ErrorAction SilentlyContinue) })
    }
    Write-Reply -Request $Request -Type 'connected' -Data $reply
}

function Invoke-Cmdlet {
    param([System.Collections.IDictionary] $Request)
    $data = $Request['data']
    $cmdlet = if ($data -is [System.Collections.IDictionary]) { [string] $data['cmdlet'] } else { '' }
    if ($script:AllowedCmdlets -notcontains $cmdlet) {
        Write-ErrorReply $Request 'cmdlet-not-allowed' "'$cmdlet' is not on the sidecar's allow-list"
        return
    }
    if (-not $script:Connected) {
        Write-ErrorReply $Request 'not-connected' 'connect first'
        return
    }
    $parameters = @{}
    if ($data['parameters'] -is [System.Collections.IDictionary]) {
        foreach ($key in $data['parameters'].Keys) { $parameters[$key] = $data['parameters'][$key] }
    }
    [string[]] $properties = if ($data['properties']) { @($data['properties']) } else { @() }

    # A non-terminating error (one mailbox of a bulk run) is collected, not thrown: the objects that did come back
    # are still the answer, with the errors next to them. A terminating error goes to the caller's catch.
    $failures = $null
    if ($data.Contains('input') -and $null -ne $data['input']) {
        $result = @($data['input']) | & $cmdlet @parameters -ErrorAction SilentlyContinue -ErrorVariable failures 6> $null
    } else {
        $result = & $cmdlet @parameters -ErrorAction SilentlyContinue -ErrorVariable failures 6> $null
    }
    $items = @($result | Where-Object { $null -ne $_ } | ForEach-Object { ConvertTo-PlainItem $_ $properties })
    $errors = @($failures | Where-Object { $null -ne $_ })
    if ($items.Count -eq 0 -and $errors.Count -gt 0) {
        $first = $errors[0]
        $message = if ($first -is [System.Management.Automation.ErrorRecord]) { $first.Exception.Message } else { [string] $first }
        Write-ErrorReply $Request 'cmdlet-failed' $message $(if ($first -is [System.Management.Automation.ErrorRecord]) { $first })
        return
    }
    $reply = [ordered]@{ items = $items; count = $items.Count }
    if ($errors.Count -gt 0) {
        $reply['errors'] = @($errors | ForEach-Object {
                Hide-Secret $(if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { [string] $_ })
            })
    }
    Write-Reply -Request $Request -Type 'result' -Data $reply
}

while ($true) {
    $line = $script:ProtocolIn.ReadLine()
    if ($null -eq $line) { break }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $request = $line | ConvertFrom-Json -AsHashtable -ErrorAction Stop
    } catch {
        [Console]::Error.WriteLine('sidecar: not a message')
        continue
    }
    if ($request -isnot [System.Collections.IDictionary] -or $request['aegis'] -ne $script:ProtocolVersion -or -not $request['type']) {
        [Console]::Error.WriteLine('sidecar: foreign message refused')
        continue
    }
    if ($request['type'] -eq 'exit') { break }
    try {
        switch ($request['type']) {
            'hello' { Invoke-Hello $request }
            'connect' { Invoke-Connect $request }
            'reconnect' { Invoke-Connect $request -Again }
            'invoke' { Invoke-Cmdlet $request }
            default { Write-ErrorReply $request 'unknown-type' "no handler for '$($request['type'])'" }
        }
    } catch {
        Write-ErrorReply $request 'cmdlet-failed' $_.Exception.Message $_
    }
}

if ($script:Connected) {
    # Drops the session and the module's temporary copy under %TMP%; a failure here changes nothing for the caller.
    try { Disconnect-ExchangeOnline -Confirm:$false 6> $null | Out-Null } catch { [Console]::Error.WriteLine('sidecar: disconnect failed') }
}
