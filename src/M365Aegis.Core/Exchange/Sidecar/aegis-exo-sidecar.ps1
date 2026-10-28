#Requires -Version 7.6
<#
.SYNOPSIS
The Exchange Online sidecar: a long-lived pwsh process the Core drives over JSON lines (D-004).

.DESCRIPTION
The Core starts this script with -NoProfile -NonInteractive and talks to it on stdin and stdout, one JSON object
per line, tagged {"aegis":1,...}. Everything the script prints that is not a protocol line goes to stderr, so a
module banner or a Write-Host cannot corrupt the channel. The scaffold version knows three messages:

  {"aegis":1,"id":"1","type":"hello"}                          -> {"aegis":1,"id":"1","type":"hello","data":{...}}
  {"aegis":1,"id":"2","type":"connect","data":{"accessToken":"...","organization":"contoso.onmicrosoft.com"}}
                                                               -> connected / error
  {"aegis":1,"id":"3","type":"invoke","data":{"cmdlet":"Get-Mailbox","parameters":{...}}}
                                                               -> result (ConvertTo-Json -Depth 6) / error
  {"aegis":1,"type":"exit"}                                    -> the process ends

Only the cmdlets of $AllowedCmdlets run; anything else is an error. The real connect and the first cmdlets arrive
with M2 (tasks 015 and 016); the scaffold answers hello, refuses an unknown cmdlet, and connects only when the
ExchangeOnlineManagement module is present. tests/sidecar/ runs this script against a fake module.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'

# The protocol owns stdout. Everything else the process would print (a module banner, a Write-Host, a stray
# Out-Default) goes to stderr: the console's standard output is swapped for its error stream, and the protocol
# writer keeps the original.
$script:ProtocolOut = [Console]::Out
[Console]::SetOut([Console]::Error)

$script:ProtocolVersion = 1
$script:AllowedCmdlets = @(
    'Get-Mailbox', 'Get-EXOMailbox', 'Get-EXOMailboxPermission', 'Get-EXORecipientPermission',
    'Get-EXOMailboxStatistics', 'Get-MailboxPermission', 'Get-RecipientPermission'
)
$script:Connected = $false

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

function ConvertTo-Hashtable {
    param($Object)
    if ($null -eq $Object) { return $null }
    if ($Object -is [hashtable]) { return $Object }
    $table = @{}
    foreach ($property in $Object.PSObject.Properties) { $table[$property.Name] = $property.Value }
    return $table
}

function Write-Protocol {
    param([hashtable] $Message)
    $Message['aegis'] = $script:ProtocolVersion
    # One line, compact, UTF-8 without BOM; -Depth 6 covers the cmdlet objects the allow-list returns.
    $line = $Message | ConvertTo-Json -Compress -Depth 6
    $script:ProtocolOut.WriteLine($line)
    $script:ProtocolOut.Flush()
}

function Write-Reply {
    param([hashtable] $Request, [string] $Type, $Data)
    $reply = @{ type = $Type }
    if ($Request.ContainsKey('id')) { $reply['id'] = $Request['id'] }
    if ($null -ne $Data) { $reply['data'] = $Data }
    Write-Protocol $reply
}

function Write-ErrorReply {
    param([hashtable] $Request, [string] $Code, [string] $Text)
    Write-Reply -Request $Request -Type 'error' -Data @{ code = $Code; message = $Text }
}

function Invoke-Hello {
    param([hashtable] $Request)
    $module = Find-ExoModule
    Write-Reply -Request $Request -Type 'hello' -Data @{
        pwsh = $PSVersionTable.PSVersion.ToString()
        module = if ($module) { $module.Version.ToString() } else { $null }
        allowedCmdlets = $script:AllowedCmdlets
    }
}

function Invoke-Connect {
    param([hashtable] $Request)
    $data = ConvertTo-Hashtable $Request['data']
    if ($null -eq $data -or -not $data['accessToken'] -or -not $data['organization']) {
        Write-ErrorReply $Request 'bad-request' 'connect needs data.accessToken and data.organization'
        return
    }
    $module = Find-ExoModule
    if ($null -eq $module) {
        Write-ErrorReply $Request 'module-missing' 'the ExchangeOnlineManagement module is not installed'
        return
    }
    # The information stream (Write-Host, the module banner) is dropped; a terminating error still throws.
    Import-Module $module.Path -ErrorAction Stop 6> $null | Out-Null
    Connect-ExchangeOnline -AccessToken $data['accessToken'] -Organization $data['organization'] -ShowBanner:$false `
        -SkipLoadingFormatData -CommandName $script:AllowedCmdlets 6> $null | Out-Null
    $script:Connected = $true
    Write-Reply -Request $Request -Type 'connected' -Data @{ organization = $data['organization'] }
}

function Invoke-Cmdlet {
    param([hashtable] $Request)
    $data = ConvertTo-Hashtable $Request['data']
    $cmdlet = if ($null -ne $data) { [string] $data['cmdlet'] } else { '' }
    if ($script:AllowedCmdlets -notcontains $cmdlet) {
        Write-ErrorReply $Request 'cmdlet-not-allowed' "'$cmdlet' is not on the sidecar's allow-list"
        return
    }
    if (-not $script:Connected) {
        Write-ErrorReply $Request 'not-connected' 'connect first'
        return
    }
    $parameters = ConvertTo-Hashtable $data['parameters']
    if ($null -eq $parameters) { $parameters = @{} }
    $result = & $cmdlet @parameters 6> $null
    Write-Reply -Request $Request -Type 'result' -Data @{ items = @($result) }
}

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $request = ConvertTo-Hashtable ($line | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        [Console]::Error.WriteLine("sidecar: not a message: $line")
        continue
    }
    if ($request['aegis'] -ne $script:ProtocolVersion -or -not $request['type']) {
        [Console]::Error.WriteLine("sidecar: foreign message refused")
        continue
    }
    try {
        switch ($request['type']) {
            'hello' { Invoke-Hello $request }
            'connect' { Invoke-Connect $request }
            'invoke' { Invoke-Cmdlet $request }
            'exit' { break }
            default { Write-ErrorReply $request 'unknown-type' "no handler for '$($request['type'])'" }
        }
    } catch {
        Write-ErrorReply $request 'cmdlet-failed' $_.Exception.Message
    }
    if ($request['type'] -eq 'exit') { break }
}
