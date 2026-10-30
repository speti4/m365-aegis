#Requires -Version 7.6
<#
.SYNOPSIS
The sidecar's Pester tests with the coverage of aegis-exo-sidecar.ps1, held at 100% of its commands (task 070).

.DESCRIPTION
Runs tests/sidecar in Pester (the version scripts/pester-version.txt names), measures the script with Pester's
profiler-based coverage (no breakpoints), writes the JaCoCo report to TestResults/sidecar-coverage.xml and fails
when a test fails or a command of the script did not run, naming each one. Pester counts commands, not branches, and
sees this process only: the tests run every scenario in process as well as in a child pwsh. Pester 5.7's
CoveragePercentTarget does not fail a run, so the threshold is checked here. The CI `sidecar` job runs this file;
locally: pwsh -File tests/sidecar/run-with-coverage.ps1
#>
[CmdletBinding()]
param(
    # The coverage the script must reach, in percent of its commands.
    [double] $Target = 100
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Import-Module Pester -RequiredVersion (Get-Content (Join-Path $repo 'scripts' 'pester-version.txt') -Raw).Trim()

$config = New-PesterConfiguration
$config.Run.Path = Join-Path $repo 'tests' 'sidecar'
$config.Run.PassThru = $true
$config.Output.Verbosity = 'Detailed'
$config.CodeCoverage.Enabled = $true
$config.CodeCoverage.Path = Join-Path $repo 'src' 'M365Aegis.Core' 'Exchange' 'Sidecar' 'aegis-exo-sidecar.ps1'
$config.CodeCoverage.UseBreakpoints = $false
$config.CodeCoverage.OutputFormat = 'JaCoCo'
$config.CodeCoverage.OutputPath = Join-Path $repo 'TestResults' 'sidecar-coverage.xml'
$config.CodeCoverage.CoveragePercentTarget = $Target
$result = Invoke-Pester -Configuration $config

if ($result.Result -ne 'Passed') {
    Write-Error "the sidecar tests did not pass: $($result.FailedCount) failed" -ErrorAction Continue
    exit 1
}
$coverage = $result.CodeCoverage
$summary = [string]::Format([cultureinfo]::InvariantCulture, 'the sidecar script: {0}/{1} commands ({2:N2}%)', $coverage.CommandsExecutedCount, $coverage.CommandsAnalyzedCount, $coverage.CoveragePercent)
if ($coverage.CoveragePercent -lt $Target) {
    foreach ($missed in $coverage.CommandsMissed) {
        Write-Error ('not covered: line {0}: {1}' -f $missed.Line, $missed.Command) -ErrorAction Continue
    }
    Write-Error "$summary, under the target of $Target%" -ErrorAction Continue
    exit 1
}
Write-Output $summary
exit 0
