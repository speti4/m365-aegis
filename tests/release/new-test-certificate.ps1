#Requires -Version 7.6
using namespace System.Security.Cryptography
using namespace System.Security.Cryptography.X509Certificates
<#
.SYNOPSIS
Writes a throwaway self-signed code-signing certificate as a PFX file, for the signing tests and the `windows` job's
dry run of the release script (task 085).

.DESCRIPTION
The certificate is made in memory (RSA 3072, SHA-256, code signing only, not a certificate authority), valid for a
day, and never enters a certificate store: nothing on the machine trusts it, which is the case the release script's
self-signed verification covers. The PFX is protected with the M365AEGIS_SIGNING_PASSWORD environment variable when
it is set, as the signing script reads it. Returns the certificate's public part.

.EXAMPLE
tests\release\new-test-certificate.ps1 -Path $env:RUNNER_TEMP\test-signing.pfx
#>
[CmdletBinding()]
param(
    # Where the PFX file goes.
    [Parameter(Mandatory)] [string] $Path,
    # The subject of the certificate.
    [string] $Subject = "CN=m365-aegis test signing (throwaway)"
)

$ErrorActionPreference = "Stop"

$rsa = [RSA]::Create(3072)
try {
    $request = [CertificateRequest]::new($Subject, $rsa, [HashAlgorithmName]::SHA256, [RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $request.CertificateExtensions.Add([X509KeyUsageExtension]::new([X509KeyUsageFlags]::DigitalSignature, $true))
    $usages = [OidCollection]::new()
    [void] $usages.Add([Oid]::new("1.3.6.1.5.5.7.3.3"))
    $request.CertificateExtensions.Add([X509EnhancedKeyUsageExtension]::new($usages, $false))
    $request.CertificateExtensions.Add([X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
    $now = [DateTimeOffset]::UtcNow
    $certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(1))
    try {
        $password = $env:M365AEGIS_SIGNING_PASSWORD
        $bytes = if ($password) { $certificate.Export([X509ContentType]::Pkcs12, $password) } else { $certificate.Export([X509ContentType]::Pkcs12) }
        $full = [System.IO.Path]::GetFullPath($Path)
        New-Item -ItemType Directory -Force (Split-Path -Parent $full) | Out-Null
        [System.IO.File]::WriteAllBytes($full, $bytes)
        [X509CertificateLoader]::LoadCertificate($certificate.RawData)
    } finally {
        $certificate.Dispose()
    }
} finally {
    $rsa.Dispose()
}
