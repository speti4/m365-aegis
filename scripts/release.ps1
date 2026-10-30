<#
.SYNOPSIS
Publishes the two exes and packs the release zip: dist\m365-aegis-<version>.zip, with its checksum and its
release notes next to it.

.DESCRIPTION
The version is the one in Directory.Build.props, the only place it is written. The zip holds the framework-
dependent, ReadyToRun win-x64 publish of M365Aegis.Cli (m365-aegis.exe) and M365Aegis.Gui (m365-aegis-gui.exe
with wwwroot\, the built front end) in one folder (D-007), the Exchange Online sidecar script under sidecar\,
THIRD-PARTY-NOTICES.md (nuget-license over the shipped packages, plus the front end's packages from the lock
file) and GUIDE.md (docs\usage.md, the guide for the support team; task 086). Next to the zip go SHA256SUMS.txt
(the zip's SHA-256 in `sha256sum -c` format) and release-notes-<version>.md (scripts\changelog.py --notes, the body
of the GitHub release, with a line on the signature). CI runs this script on a v<version> tag with -Tag set, which
must match the version; by hand the tag check is skipped. -DryRun publishes and packs into a temporary folder and
prints the sizes without writing dist\ (the `windows` CI job).

With -CertificatePath (a PFX file, its password in M365AEGIS_SIGNING_PASSWORD) the product's exes and assemblies
are signed, timestamped and verified by scripts\sign-release.ps1 before they are zipped (task 085); without it the
zip is unsigned and the output says so. -RequireSignature (the release job) fails before anything is built when no
certificate or no timestamp server is given.

Needs the .NET SDK of global.json, Node (the front end build), Python 3, git with the release tags, the
nuget-license tool of .config/dotnet-tools.json (`dotnet tool restore`), and signtool (the Windows SDK) to sign.

.EXAMPLE
scripts\release.ps1
scripts\release.ps1 -DryRun
scripts\release.ps1 -DryRun -CertificatePath test-signing.pfx -TimestampUrl ''
#>
[CmdletBinding()]
param(
    # Where the zip, the checksum file and the notes go.
    [string] $Out = "dist",
    # The git tag being released (CI); must equal "v<version>".
    [string] $Tag = "",
    # Publish and pack into a temporary folder, print the sizes, write nothing under -Out.
    [switch] $DryRun,
    # The PFX file of the signing certificate; empty for an unsigned zip. Its password is in M365AEGIS_SIGNING_PASSWORD.
    [string] $CertificatePath = "",
    # The RFC 3161 timestamp server of the signatures; empty for none (a throwaway certificate, offline).
    [string] $TimestampUrl = "http://timestamp.digicert.com",
    # Fail without a certificate or a timestamp server (the release job: a release is signed).
    [switch] $RequireSignature
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch '<Version>([^<]+)</Version>') { throw "no <Version> in Directory.Build.props" }
$version = $Matches[1].Trim()
if ($Tag -and $Tag -ne "v$version") {
    throw "tag '$Tag' does not match the Directory.Build.props version 'v$version'"
}
if ($RequireSignature -and -not $CertificatePath) {
    throw "-RequireSignature without -CertificatePath: a release is signed (the release job reads the SIGNING_CERTIFICATE secret; docs/development.md, The release)"
}
if ($RequireSignature -and -not $TimestampUrl) { throw "-RequireSignature without -TimestampUrl: the signatures of a release are timestamped" }
if ($CertificatePath -and -not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "no certificate file at '$CertificatePath'" }

# Python as `python`, else through the `py` launcher, which is all some Windows installs put on PATH.
$python = (Get-Command python -ErrorAction SilentlyContinue).Source
$pythonArgs = @()
if (-not $python) {
    $python = (Get-Command py -ErrorAction SilentlyContinue).Source
    $pythonArgs = @("-3")
}
if (-not $python) { throw "python not found (neither python nor py on PATH)" }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "m365-aegis-release-$version"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
$outDir = if ($DryRun) { Join-Path $stage "out" } else { (New-Item -ItemType Directory -Force $Out).FullName }
New-Item -ItemType Directory -Force $outDir | Out-Null

# What can fail fast runs before the publish: the release notes and the license tool.
$notes = Join-Path $outDir "release-notes-$version.md"
& $python @pythonArgs -I (Join-Path $root "scripts\changelog.py") --notes $version --out $notes
if ($LASTEXITCODE -ne 0) { throw "release notes failed ($LASTEXITCODE)" }
& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" }
# nuget-license reads every project of the solution, the test projects included, before -exclude-projects drops them,
# so each needs its project.assets.json: the whole solution is restored here, not only the two published projects.
# (The first tag release, v0.1.0 on 2026-10-30, failed on the missing test assets in the release job.)
& dotnet restore --locked-mode (Join-Path $root "M365Aegis.slnx") --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)" }

# One publish folder for both exes (D-007): framework-dependent, ReadyToRun, win-x64, the shared assemblies once.
# No XML documentation files, neither ours nor the packages' (task 031): an editor reads them, nobody does in the zip.
$publish = Join-Path $stage "publish"
foreach ($project in @("src\M365Aegis.Cli\M365Aegis.Cli.csproj", "src\M365Aegis.Gui\M365Aegis.Gui.csproj")) {
    & dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project ($LASTEXITCODE)" }
}
$xml = @(Get-ChildItem $publish -Filter *.xml -Recurse -File)
if ($xml.Count -gt 0) { throw "documentation files in the publish folder: $($xml.Name -join ', ')" }
# msalruntime.dll is the broker's native half (Microsoft.Identity.Client.NativeInterop); a win-x64 publish puts it
# next to the exes, and the sign-in fails without it.
foreach ($file in @("m365-aegis.exe", "m365-aegis-gui.exe", "msalruntime.dll", "wwwroot\index.html", "sidecar\aegis-exo-sidecar.ps1")) {
    if (-not (Test-Path (Join-Path $publish $file))) { throw "missing in the publish folder: $file" }
}

# The exe carries the version Directory.Build.props names; a stale build or a wrong props edit shows here.
$stamped = (Get-Item (Join-Path $publish "m365-aegis.exe")).VersionInfo.ProductVersion -replace '\+.*$', ''
if ($stamped -ne $version) {
    throw "m365-aegis.exe carries version '$stamped', Directory.Build.props says '$version'"
}

# The signature (task 085): the product's own exes and assemblies, before the zip and its checksum are made.
if ($CertificatePath) {
    $signed = & (Join-Path $root "scripts\sign-release.ps1") -Folder $publish -CertificatePath $CertificatePath -TimestampUrl $TimestampUrl
    $trust = if ($signed.Trusted) { "trusted here" } else { "self-signed and not trusted here, verified against itself" }
    $stamp = if ($signed.Timestamped) { "timestamped" } else { "no timestamp" }
    $signing = "$($signed.Signer) ($($signed.Thumbprint)), $stamp, $trust; $($signed.Files -join ', ')"
    $timestampNote = if ($signed.Timestamped) { ", with a timestamp" } else { "" }
    $notesLine = "The exes and the product's assemblies are signed by ``$($signed.Signer)`` (certificate thumbprint " +
        "``$($signed.Thumbprint)``)$timestampNote. In the unzipped folder ``Get-AuthenticodeSignature .\m365-aegis.exe`` " +
        "names the signer, and ``m365-aegis doctor`` checks every signed file (its ``signature`` row)."
} else {
    $signing = "none: no -CertificatePath, so the zip is unsigned"
    $notesLine = "The exes in this zip are not signed."
}
Add-Content -LiteralPath $notes -Value @("", $notesLine) -Encoding utf8NoBOM

# The notices: the NuGet packages shipped with the exes (nuget-license, the allowlist of licenses/allowed.json, the
# license texts of licenses/license-texts.json for a package that ships its license as a file), then the front
# end's packages from the lock file (scripts\npm-licenses.py, the same allowlist).
$notices = Join-Path $publish "THIRD-PARTY-NOTICES.md"
& dotnet nuget-license -i (Join-Path $root "M365Aegis.slnx") -t -a (Join-Path $root "licenses\allowed.json") -ignore (Join-Path $root "licenses\ignored.json") -file-mapping (Join-Path $root "licenses\license-texts.json") -exclude-projects "*.Tests" -o Markdown -fo $notices
if ($LASTEXITCODE -ne 0) { throw "nuget-license failed ($LASTEXITCODE)" }
& $python @pythonArgs -I (Join-Path $root "scripts\npm-licenses.py") --notices $notices
if ($LASTEXITCODE -ne 0) { throw "npm-licenses failed ($LASTEXITCODE)" }
# The guide is the support team's (docs\usage.md, task 086), who have the zip and no repo; the README is a contributor's.
Copy-Item (Join-Path $root "docs\usage.md") (Join-Path $publish "GUIDE.md")

$zip = Join-Path $outDir "m365-aegis-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

# "<sha256>  <file name>" and a LF, as sha256sum writes it, so `sha256sum -c SHA256SUMS.txt` checks the download.
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
$sums = Join-Path $outDir "SHA256SUMS.txt"
[System.IO.File]::WriteAllText($sums, "$hash  m365-aegis-$version.zip`n", (New-Object System.Text.UTF8Encoding $false))

$publishMb = [math]::Round(((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output "version:  $version (exe properties: $stamped)"
Write-Output "signing:  $signing"
Write-Output "publish:  $publish ($publishMb MB unpacked)"
Write-Output "zip:      $zip ($zipMb MB)"
Write-Output "sha256:   $hash ($sums)"
Write-Output "notes:    $notes"
if ($DryRun) {
    Write-Output "dry run: nothing written under $Out"
    Remove-Item -Recurse -Force $stage
} else {
    Remove-Item -Recurse -Force $publish
}
