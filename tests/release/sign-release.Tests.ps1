#Requires -Version 7.6
<#
The signing of the release (task 085): scripts/sign-release.ps1 over copies of the built command line's files, with a
throwaway self-signed certificate that new-test-certificate.ps1 makes and nothing on the machine trusts, and the
release script's refusal to build a release without a certificate. Windows only (signtool and Authenticode); the
`windows` job runs these after its build, which makes the files copied here. No timestamp: the tests never touch the
network.
#>

BeforeDiscovery {
    $script:skip = -not $IsWindows
    # Trusting the throwaway certificate takes the local machine's root store, which needs an elevated session: the
    # `windows` job's runner is one, a contributor's shell usually is not (task 093).
    $script:elevated = $IsWindows -and
        ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

BeforeAll {
    $script:repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:sign = Join-Path $script:repo 'scripts' 'sign-release.ps1'
    $script:built = Join-Path $script:repo 'src' 'M365Aegis.Cli' 'bin' 'Release' 'net10.0'
    if (-not (Test-Path (Join-Path $script:built 'm365-aegis.exe'))) {
        throw "build the command line first (dotnet build -c Release): no m365-aegis.exe under $script:built"
    }
    $script:work = Join-Path ([System.IO.Path]::GetTempPath()) "m365-aegis-sign-tests-$PID"
    New-Item -ItemType Directory -Force $script:work | Out-Null
    $script:pfx = Join-Path $script:work 'test-signing.pfx'
    $script:certificate = & (Join-Path $PSScriptRoot 'new-test-certificate.ps1') -Path $script:pfx

    # A folder as the publish leaves it: the product's files, a package's assembly, and the sidecar script.
    function Copy-PublishFolder([string] $name) {
        $folder = Join-Path $script:work $name
        New-Item -ItemType Directory -Force (Join-Path $folder 'sidecar') | Out-Null
        foreach ($file in @('m365-aegis.exe', 'm365-aegis.dll', 'M365Aegis.Core.dll', 'System.CommandLine.dll')) {
            Copy-Item (Join-Path $script:built $file) $folder
        }
        Copy-Item (Join-Path $script:built 'sidecar' 'aegis-exo-sidecar.ps1') (Join-Path $folder 'sidecar')
        return $folder
    }
}

AfterAll {
    if ($script:work -and (Test-Path $script:work)) { Remove-Item -Recurse -Force $script:work }
}

Describe 'sign-release.ps1' -Skip:$skip {
    It 'signs the product files with the certificate and verifies them against it' {
        $folder = Copy-PublishFolder 'signed'
        $commandLine = (Get-AuthenticodeSignature (Join-Path $folder 'System.CommandLine.dll')).SignerCertificate.Thumbprint

        $result = & $script:sign -Folder $folder -CertificatePath $script:pfx -TimestampUrl ''

        $result.Files | Should -Be @('m365-aegis.dll', 'm365-aegis.exe', 'M365Aegis.Core.dll')
        $result.Thumbprint | Should -Be $script:certificate.Thumbprint
        $result.Signer | Should -Be $script:certificate.Subject
        $result.Trusted | Should -BeFalse
        $result.Timestamped | Should -BeFalse
        $LASTEXITCODE | Should -Be 0
        foreach ($file in $result.Files) {
            $signature = Get-AuthenticodeSignature (Join-Path $folder $file)
            $signature.SignerCertificate.Thumbprint | Should -Be $script:certificate.Thumbprint
            # Not trusted on this machine, but the hash holds.
            $signature.Status | Should -Not -BeIn @('NotSigned', 'HashMismatch')
        }
        # A package's assembly keeps its publisher's signature, and the sidecar script stays as the build wrote it.
        (Get-AuthenticodeSignature (Join-Path $folder 'System.CommandLine.dll')).SignerCertificate.Thumbprint | Should -Be $commandLine
        (Get-AuthenticodeSignature (Join-Path $folder 'sidecar' 'aegis-exo-sidecar.ps1')).Status | Should -Be 'NotSigned'
    }

    It 'gives the doctor a signer it names and does not trust, and a changed file it fails' {
        # The whole command line folder, so the signed exe runs its doctor over its own files (WinVerifyTrust for real).
        $folder = Join-Path $script:work 'doctor'
        Copy-Item -Recurse $script:built $folder
        $null = & $script:sign -Folder $folder -CertificatePath $script:pfx -TimestampUrl ''
        $env:M365AEGIS_HOME = Join-Path $script:work 'home'
        try {
            $check = ((& (Join-Path $folder 'm365-aegis.exe') --json doctor --offline | ConvertFrom-Json).data.checks |
                Where-Object name -EQ 'signature')
            $check.status | Should -Be 'warn'
            $check.detail | Should -BeLike "*(0x800B0109)*signed by $($script:certificate.Subject), which this machine does not trust"
            # Not the pinned release certificate either, so the hint does not ask an administrator to trust it (task 093).
            $check.hint | Should -BeLike "the signer is not the release's certificate (thumbprint *)*"

            # Pinned through the test seam, the same untrusted signer is the one an administrator would trust.
            $env:M365AEGIS_TEST_RELEASE_THUMBPRINT = $script:certificate.Thumbprint
            try {
                $check = ((& (Join-Path $folder 'm365-aegis.exe') --json doctor --offline | ConvertFrom-Json).data.checks |
                    Where-Object name -EQ 'signature')
            } finally {
                Remove-Item Env:\M365AEGIS_TEST_RELEASE_THUMBPRINT
            }
            $check.status | Should -Be 'warn'
            $check.hint | Should -BeLike 'an administrator trusts the release*'

            # One byte of the DOS stub's text: covered by the signature, ignored by the loader, so the exe still runs.
            $dll = Join-Path $folder 'm365-aegis.dll'
            $bytes = [System.IO.File]::ReadAllBytes($dll)
            $bytes[0x50] = $bytes[0x50] -bxor 0x20
            [System.IO.File]::WriteAllBytes($dll, $bytes)
            (Get-AuthenticodeSignature $dll).Status | Should -Be 'HashMismatch'

            $check = ((& (Join-Path $folder 'm365-aegis.exe') --json doctor --offline | ConvertFrom-Json).data.checks |
                Where-Object name -EQ 'signature')
            $check.status | Should -Be 'fail'
            $check.detail | Should -Be 'm365-aegis.dll (0x80096010): the signature does not hold, the file changed after it was signed'
        } finally {
            Remove-Item Env:\M365AEGIS_HOME
        }
    }

    It 'refuses a certificate file that does not open with the password' {
        $folder = Copy-PublishFolder 'password'
        $env:M365AEGIS_SIGNING_PASSWORD = 'not-the-password'
        try {
            { & $script:sign -Folder $folder -CertificatePath $script:pfx -TimestampUrl '' } | Should -Throw '*does not open with the password*'
        } finally {
            Remove-Item Env:\M365AEGIS_SIGNING_PASSWORD
        }
        (Get-AuthenticodeSignature (Join-Path $folder 'm365-aegis.exe')).Status | Should -Be 'NotSigned'
    }

    It 'signs with a certificate file protected by the password of the environment' {
        $folder = Copy-PublishFolder 'protected'
        $env:M365AEGIS_SIGNING_PASSWORD = 'throwaway-password'
        try {
            $protected = Join-Path $script:work 'protected.pfx'
            $made = & (Join-Path $PSScriptRoot 'new-test-certificate.ps1') -Path $protected
            $result = & $script:sign -Folder $folder -CertificatePath $protected -TimestampUrl ''
        } finally {
            Remove-Item Env:\M365AEGIS_SIGNING_PASSWORD
        }
        $result.Thumbprint | Should -Be $made.Thumbprint
    }

    It 'refuses a folder without a product file and a missing certificate file' {
        $empty = Join-Path $script:work 'empty'
        New-Item -ItemType Directory -Force $empty | Out-Null
        { & $script:sign -Folder $empty -CertificatePath $script:pfx -TimestampUrl '' } | Should -Throw '*no m365-aegis exe or assembly*'
        { & $script:sign -Folder $empty -CertificatePath (Join-Path $script:work 'missing.pfx') -TimestampUrl '' } | Should -Throw '*no certificate file*'
    }
}

Describe 'the doctor pins the release certificate' -Skip:(-not $elevated) {
    # Task 093: with the throwaway certificate trusted on this machine (a root of the local machine for the length of
    # these tests), a signature that holds is a pass only for the pinned certificate. The real one is not on any
    # developer machine, so the pin is moved to the throwaway certificate through the test seam.
    BeforeAll {
        $script:root = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine')
        $script:root.Open('ReadWrite')
        $script:root.Add($script:certificate)
        $script:trusted = Join-Path $script:work 'trusted'
        Copy-Item -Recurse $script:built $script:trusted
        $null = & $script:sign -Folder $script:trusted -CertificatePath $script:pfx -TimestampUrl ''
        $env:M365AEGIS_HOME = Join-Path $script:work 'home'

        function Get-SignatureRow {
            (& (Join-Path $script:trusted 'm365-aegis.exe') --json doctor --offline | ConvertFrom-Json).data.checks |
                Where-Object name -EQ 'signature'
        }
    }

    AfterAll {
        if ($script:root) {
            $script:root.Remove($script:certificate)
            $script:root.Dispose()
        }
        Remove-Item Env:\M365AEGIS_HOME -ErrorAction Ignore
    }

    It 'warns with the signer and its thumbprint when the trusted certificate is not the pinned one' {
        $row = Get-SignatureRow

        $row.status | Should -Be 'warn'
        $row.detail | Should -BeLike ("m365-aegis.dll, m365-aegis.exe, M365Aegis.Core.dll: signed by $($script:certificate.Subject) " +
            "($($script:certificate.Thumbprint)), not the release's certificate (thumbprint *)")
    }

    It 'passes when the trusted certificate is the pinned one' {
        $env:M365AEGIS_TEST_RELEASE_THUMBPRINT = $script:certificate.Thumbprint
        try {
            $row = Get-SignatureRow
        } finally {
            Remove-Item Env:\M365AEGIS_TEST_RELEASE_THUMBPRINT
        }

        $row.status | Should -Be 'pass'
        $row.detail | Should -Be "3 file(s) signed by $($script:certificate.Subject), the certificate M365AEGIS_TEST_RELEASE_THUMBPRINT pins"
    }
}

Describe 'release.ps1 -RequireSignature' -Skip:$skip {
    # The checks run before the release notes and the publish, so these return in a moment.
    It 'fails without a certificate, as the release job does without its secret' {
        { & (Join-Path $script:repo 'scripts' 'release.ps1') -DryRun -RequireSignature } | Should -Throw '*-RequireSignature without -CertificatePath*'
    }

    It 'fails without a timestamp server' {
        { & (Join-Path $script:repo 'scripts' 'release.ps1') -DryRun -RequireSignature -CertificatePath $script:pfx -TimestampUrl '' } |
            Should -Throw '*without -TimestampUrl*'
    }
}
