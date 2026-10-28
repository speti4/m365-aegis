using M365Aegis.Core.Diagnostics;

namespace M365Aegis.Core.Tests;

public sealed class DoctorTests
{
    [Fact]
    public void MissingPwshFailsTheReport()
    {
        string home = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));

        DoctorReport report = Doctor.RunOffline(home, pwshPath: null);

        Assert.False(report.Healthy);
        DoctorCheck pwsh = Assert.Single(report.Checks, check => check.Name == "pwsh");
        Assert.Equal(CheckStatus.Fail, pwsh.Status);
        Assert.True(pwsh.Required);
    }

    [Fact]
    public void MissingHomeIsAWarningNotAFailure()
    {
        string home = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));

        DoctorReport report = Doctor.RunOffline(home, pwshPath: null);

        DoctorCheck homeCheck = Assert.Single(report.Checks, check => check.Name == "home");
        Assert.Equal(CheckStatus.Warn, homeCheck.Status);
        Assert.Equal(home, report.Home);
    }

    [Fact]
    public void TheNetworkChecksAreSkippedOffline()
    {
        DoctorReport report = Doctor.RunOffline(Path.GetTempPath(), pwshPath: null);

        DoctorCheck signIn = Assert.Single(report.Checks, check => check.Name == "sign-in");
        Assert.Equal(CheckStatus.Skipped, signIn.Status);
    }

    [Fact]
    public void ThePwshOnThisMachineIsReadIfPresent()
    {
        // No assumption about the machine: with pwsh on PATH the version is read, without it the check fails.
        string? pwsh = Doctor.FindPwsh();
        DoctorReport report = Doctor.RunOffline(Path.GetTempPath(), pwsh);
        DoctorCheck check = Assert.Single(report.Checks, check => check.Name == "pwsh");

        if (pwsh is null)
        {
            Assert.Equal(CheckStatus.Fail, check.Status);
        }
        else
        {
            Assert.NotEqual(CheckStatus.Skipped, check.Status);
            Assert.Contains(pwsh, check.Detail, StringComparison.Ordinal);
        }
    }
}
