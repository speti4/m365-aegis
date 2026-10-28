#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.7.0' }
<#
The Exchange Online sidecar end to end: a real pwsh child process running aegis-exo-sidecar.ps1 with the fake
ExchangeOnlineManagement module first on PSModulePath. Every answer is read back as a protocol line; the fake
module's Write-Host noise must not appear among them. Runs on Linux and Windows alike (the CI `sidecar` job and
the local checks of CONTRIBUTING.md).
#>

BeforeAll {
    $script:Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Sidecar = Join-Path $script:Repo 'src' 'M365Aegis.Core' 'Exchange' 'Sidecar' 'aegis-exo-sidecar.ps1'
    $script:FakeModules = Join-Path $PSScriptRoot 'modules' 'ExchangeOnlineManagement'

    function Invoke-Sidecar {
        # Feeds the lines to a fresh sidecar and returns its stdout lines (the protocol) and stderr (the noise).
        param([string[]] $Lines, [switch] $WithoutModule)
        $moduleRoot = if ($WithoutModule) { Join-Path $PSScriptRoot 'no-such-folder' } else { Split-Path $script:FakeModules -Parent }
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = (Get-Process -Id $PID).Path
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $script:Sidecar)) { $startInfo.ArgumentList.Add($argument) }
        $startInfo.RedirectStandardInput = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.UseShellExecute = $false
        # The sidecar looks for ExchangeOnlineManagement under this root only, so the machine's real module (if any)
        # is never imported by a test and a missing module is a deterministic case.
        $startInfo.Environment['M365AEGIS_EXO_MODULE_ROOT'] = $moduleRoot
        $process = [System.Diagnostics.Process]::Start($startInfo)
        foreach ($line in $Lines) { $process.StandardInput.WriteLine($line) }
        $process.StandardInput.WriteLine('{"aegis":1,"type":"exit"}')
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Lines = @($stdout -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
            Stderr = $stderr
        }
    }

    $script:Connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","organization":"contoso.onmicrosoft.com"}}'
}

Describe 'the sidecar protocol' {
    It 'answers hello with the pwsh version and the allow-list' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"1","type":"hello"}')
        $run.ExitCode | Should -Be 0
        $run.Lines.Count | Should -Be 1
        $run.Lines[0].type | Should -Be 'hello'
        $run.Lines[0].id | Should -Be '1'
        $run.Lines[0].aegis | Should -Be 1
        $run.Lines[0].data.pwsh | Should -Match '^7\.'
        $run.Lines[0].data.allowedCmdlets | Should -Contain 'Get-Mailbox'
    }

    It 'refuses foreign text without answering it' {
        $run = Invoke-Sidecar -Lines @('not json', '{"aegis":2,"type":"hello"}', '{"aegis":1,"id":"1","type":"hello"}')
        $run.Lines.Count | Should -Be 1
        $run.Stderr | Should -Match 'not a message'
        $run.Stderr | Should -Match 'foreign message refused'
    }

    It 'names an unknown type' {
        $run = Invoke-Sidecar -Lines @('{"aegis":1,"id":"9","type":"frobnicate"}')
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'unknown-type'
    }
}

Describe 'connect and invoke against the fake module' {
    It 'connects and keeps the module noise off the protocol channel' {
        $run = Invoke-Sidecar -Lines @($script:Connect)
        $run.Lines.Count | Should -Be 1
        $run.Lines[0].type | Should -Be 'connected'
        $run.Lines[0].data.organization | Should -Be 'contoso.onmicrosoft.com'
    }

    It 'runs an allowed cmdlet and returns its objects' {
        $invoke = '{"aegis":1,"id":"3","type":"invoke","data":{"cmdlet":"Get-Mailbox","parameters":{"Identity":"shared-finance"}}}'
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines.Count | Should -Be 2
        $run.Lines[1].type | Should -Be 'result'
        $run.Lines[1].data.items.Count | Should -Be 1
        $run.Lines[1].data.items[0].PrimarySmtpAddress | Should -Be 'finance@contoso.onmicrosoft.com'
    }

    It 'refuses a cmdlet that is not on the allow-list' {
        $invoke = '{"aegis":1,"id":"3","type":"invoke","data":{"cmdlet":"Remove-Mailbox","parameters":{"Identity":"alice"}}}'
        $run = Invoke-Sidecar -Lines @($script:Connect, $invoke)
        $run.Lines[1].type | Should -Be 'error'
        $run.Lines[1].data.code | Should -Be 'cmdlet-not-allowed'
    }

    It 'refuses to invoke before a connect' {
        $invoke = '{"aegis":1,"id":"3","type":"invoke","data":{"cmdlet":"Get-Mailbox"}}'
        $run = Invoke-Sidecar -Lines @($invoke)
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'not-connected'
    }

    It 'reports a missing module instead of connecting' {
        $run = Invoke-Sidecar -Lines @($script:Connect) -WithoutModule
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'module-missing'
    }

    It 'turns a cmdlet failure into an error reply' {
        $connect = '{"aegis":1,"id":"2","type":"connect","data":{"accessToken":"fake-token","organization":"fabrikam.onmicrosoft.com"}}'
        $run = Invoke-Sidecar -Lines @($connect)
        $run.Lines[0].type | Should -Be 'error'
        $run.Lines[0].data.code | Should -Be 'cmdlet-failed'
        $run.Lines[0].data.message | Should -Match 'unknown organization'
    }
}
