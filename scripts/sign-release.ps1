#Requires -Version 7.6
<#
.SYNOPSIS
Signs the product's own exes and assemblies in a publish folder with signtool, timestamps them and verifies them
(task 085).

.DESCRIPTION
The files are m365-aegis*.exe, m365-aegis*.dll and M365Aegis.*.dll at the top of -Folder: the code built from this
repo, the same names the doctor's `signature` row reads (Authenticode.ProductFiles in the Core). The packages'
assemblies keep their publishers' own signatures, and the sidecar script stays unsigned: the doctor checks it against
the copy compiled into the Core, and a signature block would change its hash.

The certificate is a PFX file (-CertificatePath) whose password is in the M365AEGIS_SIGNING_PASSWORD environment
variable (none when it is unset), so the password is neither a parameter nor in the output. signtool signs with
SHA-256 and, with -TimestampUrl, adds an RFC 3161 timestamp, so a signature stays valid after the certificate
expires. Then every file is verified: `signtool verify /pa` passes when the certificate chains to a root this machine
trusts. A self-signed certificate this machine does not trust (a CI runner, a contributor's machine) is checked
against itself instead: the file's hash holds, the signer is this certificate, and its chain builds with the
certificate as the only trusted root; a team machine that trusts the certificate sees a valid signature.

Returns one object: Signer (the subject), Thumbprint, Files (the names signed), Timestamped, and Trusted (whether this
machine trusts the certificate). Needs signtool from the Windows SDK, which the windows-latest runner has.

.EXAMPLE
$env:M365AEGIS_SIGNING_PASSWORD = '<the password>'; scripts\sign-release.ps1 -Folder <publish> -CertificatePath signing.pfx
#>
[CmdletBinding()]
param(
    # The publish folder whose top-level product files are signed.
    [Parameter(Mandatory)] [string] $Folder,
    # The PFX file with the signing certificate and its private key.
    [Parameter(Mandatory)] [string] $CertificatePath,
    # The RFC 3161 timestamp server; empty for no timestamp (the tests, which never touch the network).
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
# signtool's exit code is read below; a non-zero one is an answer, not an error of the shell.
$PSNativeCommandUseErrorActionPreference = $false

$codeSigning = "1.3.6.1.5.5.7.3.3"
$productFile = '^(?:m365-aegis[A-Za-z0-9-]*\.(?:exe|dll)|M365Aegis\.[A-Za-z0-9.]+\.dll)$'

function Find-SignTool {
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $found = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+){3}$' -and (Test-Path (Join-Path $_.FullName "x64\signtool.exe")) } |
        Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1
    if (-not $found) { throw "signtool.exe not found: install the Windows SDK's signing tools, or put signtool on PATH" }
    return Join-Path $found.FullName "x64\signtool.exe"
}

if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "no certificate file at '$CertificatePath'" }
$CertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
$password = $env:M365AEGIS_SIGNING_PASSWORD
try {
    $pfx = [System.Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile(
        $CertificatePath, $password, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
} catch [System.Security.Cryptography.CryptographicException] {
    throw "the certificate file does not open with the password of M365AEGIS_SIGNING_PASSWORD: $($_.Exception.Message)"
}
if (-not $pfx.HasPrivateKey) { throw "the certificate file holds no private key" }
$eku = $pfx.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
if (-not ($eku -and ($eku.EnhancedKeyUsages | Where-Object Value -EQ $codeSigning))) {
    throw "the certificate '$($pfx.Subject)' is not for code signing (no enhanced key usage $codeSigning)"
}
if ($pfx.NotAfter -lt (Get-Date)) { throw "the certificate '$($pfx.Subject)' expired on $($pfx.NotAfter.ToString('yyyy-MM-dd'))" }
$certificate = [System.Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificate($pfx.RawData)
$pfx.Dispose()

$files = @(Get-ChildItem -LiteralPath $Folder -File | Where-Object { $_.Name -match $productFile } | Sort-Object Name)
if ($files.Count -eq 0) { throw "no m365-aegis exe or assembly in '$Folder'" }
$signtool = Find-SignTool

$signArguments = @("sign", "/fd", "SHA256", "/f", $CertificatePath, "/d", "m365-aegis", "/du", "https://github.com/speti4/m365-aegis")
if ($password) { $signArguments += @("/p", $password) }
if ($TimestampUrl) { $signArguments += @("/tr", $TimestampUrl, "/td", "SHA256") }
# A timestamp server can be busy for a moment; signing again replaces the signature, so a retry is safe.
$attempts = if ($TimestampUrl) { 3 } else { 1 }
for ($attempt = 1; $attempt -le $attempts; $attempt++) {
    $output = & $signtool @signArguments @($files.FullName) 2>&1
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -eq $attempts) {
        # The output names the files and the reason; the password is never in it.
        throw "signtool sign failed ($LASTEXITCODE):`n$($output -join "`n")"
    }
    Start-Sleep -Seconds (5 * $attempt)
}

$trusted = $true
foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -in @("NotSigned", "HashMismatch", "NotSupportedFileFormat", "Incompatible")) {
        throw "$($file.Name): $($signature.Status) after signing ($($signature.StatusMessage))"
    }
    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "$($file.Name): signed by '$($signature.SignerCertificate.Subject)', not by the certificate given"
    }
    if ($TimestampUrl -and -not $signature.TimeStamperCertificate) { throw "$($file.Name): signed without a timestamp" }

    $verify = & $signtool verify /pa $file.FullName 2>&1
    if ($LASTEXITCODE -eq 0) { continue }
    if ($certificate.Subject -ne $certificate.Issuer) {
        throw "$($file.Name): signtool verify /pa failed ($LASTEXITCODE):`n$($verify -join "`n")"
    }
    # Self-signed and not trusted here: the chain with the certificate as its only root, for code signing.
    $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.TrustMode = [System.Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        [void] $chain.ChainPolicy.CustomTrustStore.Add($certificate)
        [void] $chain.ChainPolicy.ApplicationPolicy.Add([System.Security.Cryptography.Oid]::new($codeSigning))
        if (-not $chain.Build($signature.SignerCertificate)) {
            $reasons = ($chain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }) -join "; "
            throw "$($file.Name): the signature does not chain to the certificate given: $reasons"
        }
    } finally {
        $chain.Dispose()
    }
    $trusted = $false
}

# The failed `signtool verify` of an untrusted self-signed certificate is an expected answer; it must not become the
# exit code of a caller that ends on this script (a CI step ends with $LASTEXITCODE).
$global:LASTEXITCODE = 0
[pscustomobject] @{
    Signer = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    Files = @($files.Name)
    Timestamped = [bool] $TimestampUrl
    Trusted = $trusted
}
