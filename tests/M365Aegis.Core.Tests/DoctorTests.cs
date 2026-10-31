using System.Text;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// Every doctor check over a faked machine (task 008): a pass, a warn and a fail case each where the check has
/// them, the module, the registry, dsregcmd, the sidecar's two copies, the sign-in and the endpoints all scripted.
/// </summary>
public sealed class DoctorTests
{
    /// <summary>The access masks of <c>FileSystemRights.FullControl</c> and <c>ReadAndExecute</c> (with <c>Synchronize</c>).</summary>
    private const int FullControl = 0x1F01FF;
    private const int ReadAndExecute = 0x1200A9;
    private const int Modify = 0x1301BF;

    private static readonly byte[] Script = Encoding.UTF8.GetBytes("#Requires -Version 7.6\nparam()\n'hello'\n");

    [Fact]
    public async Task AHealthyMachinePassesEveryCheck()
    {
        DoctorEnvironment environment = Fake(WithProfile());

        DoctorReport report = await Doctor.RunAsync(environment, offline: false, CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.All(report.Checks, check => Assert.Equal(CheckStatus.Pass, check.Status));
        Assert.Equal(
            ["home", "home-access", "dotnet-runtime", "dotnet-desktop-runtime", "pwsh", "exchange-online-module", "sidecar-script", "signature", "webview2-runtime", "config", "device", "sign-in", "consent", "endpoint-login", "endpoint-graph", "endpoint-exchange"],
            report.Checks.Select(check => check.Name));
        Assert.All(report.Checks, check => Assert.Null(check.Hint));
        Assert.Equal(ExitCode.Ok, report.ToResult().ExitCode);
    }

    [Fact]
    public async Task OfflineSkipsTheSignInAndTheEndpointsWithoutCallingThem()
    {
        bool called = false;
        DoctorEnvironment environment = Fake(WithProfile(), signIn: (_, _) => { called = true; return Task.FromResult(AuthSamples.SignedIn); }) with
        {
            Probe = (_, _) => { called = true; return Task.FromResult(new EndpointAnswer(200, TimeSpan.Zero, null)); },
        };

        DoctorReport report = await Doctor.RunAsync(environment, offline: true, CancellationToken.None);

        Assert.False(called);
        Assert.All(report.Checks.Where(check => check.Name is "sign-in" or "consent" || check.Name.StartsWith("endpoint-", StringComparison.Ordinal)),
            check => Assert.Equal(CheckStatus.Skipped, check.Status));
        Assert.True(report.Healthy);
    }

    [Fact]
    public void Home()
    {
        string home = WithProfile();
        Assert.Equal(CheckStatus.Pass, Doctor.CheckHome(home).Status);

        DoctorCheck missing = Doctor.CheckHome(ProfileStoreTests.TempHome());
        Assert.Equal(CheckStatus.Warn, missing.Status);
        Assert.True(missing.Required);

        string file = Path.Combine(home, "not-a-folder");
        File.WriteAllText(file, "");
        DoctorCheck failed = Doctor.CheckHome(file);
        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.Contains("M365AEGIS_HOME", failed.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void AHomeOnlyTheUserSystemAndAdministratorsCanReadPasses()
    {
        // Authenticated Users may add folders (no read right), and a deny entry is no read right either.
        DoctorEnvironment environment = Fake(WithProfile()) with
        {
            ReadHomeAccess = _ => UserOnly(
                new("S-1-5-11", Allow: true, Rights: 0x4, Inherited: true),
                new("S-1-5-32-545", Allow: false, ReadAndExecute, Inherited: false)),
        };

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(new DoctorCheck("home-access", CheckStatus.Pass, "only you, SYSTEM and Administrators can read it", Required: false), check);
    }

    [Fact]
    public void AnotherAccountThatCanReadTheHomeWarnsNamingItsRight()
    {
        // Task 088: a sandbox group or another user with an inherited right is named, with its right; a SID that does not
        // resolve is named by itself; the user's own SID in another case, SYSTEM and Administrators are not judged.
        string home = WithProfile();
        DoctorEnvironment environment = Fake(home) with
        {
            ReadHomeAccess = _ => UserOnly(
                new("S-1-15-2-1", Allow: true, ReadAndExecute, Inherited: true, @"APPLICATION PACKAGE AUTHORITY\ALL APPLICATION PACKAGES"),
                new("S-1-5-21-0-0-0-1002", Allow: true, Modify, Inherited: true, @"CONTOSO\bob"),
                new("S-1-5-21-0-0-0-1003", Allow: true, 0x1, Inherited: false),
                new("s-1-5-21-0-0-0-1001", Allow: true, ReadAndExecute, Inherited: true, @"CONTOSO\alice"),
                new("S-1-5-18", Allow: true, FullControl, Inherited: true, @"NT AUTHORITY\SYSTEM")),
        };

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal(
            @"APPLICATION PACKAGE AUTHORITY\ALL APPLICATION PACKAGES (read, inherited), CONTOSO\bob (modify, inherited), S-1-5-21-0-0-0-1003 (read) can read it: an account other than you, SYSTEM and Administrators can read the profiles, the plan records and the logs",
            check.Detail);
        Assert.Equal(
            $"icacls \"{home}\" /inheritance:r /grant:r \"CONTOSO\\alice:(OI)(CI)F\" \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11 *S-1-5-21-0-0-0-1003",
            check.Hint);
    }

    [Fact]
    public void AnAccountWithSeveralEntriesIsNamedOnceWithItsStrongestRight()
    {
        // Two entries of one account add up, and an explicit one among them makes it more than inherited; one of the
        // broad groups among the readers makes it every account on the machine.
        DoctorEnvironment environment = Fake(WithProfile()) with
        {
            ReadHomeAccess = _ => UserOnly(
                new("S-1-5-21-0-0-0-1002", Allow: true, ReadAndExecute, Inherited: true),
                new("S-1-5-21-0-0-0-1002", Allow: true, Modify, Inherited: false, @"CONTOSO\bob"),
                new("S-1-5-32-545", Allow: true, ReadAndExecute, Inherited: true, @"BUILTIN\Users")),
        };

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(
            @"CONTOSO\bob (modify), Users (read, inherited) can read it: every account on this machine can read the profiles, the plan records and the logs",
            check.Detail);
        Assert.EndsWith("/remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11 *S-1-5-21-0-0-0-1002", check.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x10000000, "full control")]
    [InlineData(FullControl, "full control")]
    [InlineData(Modify, "modify")]
    [InlineData(ReadAndExecute, "read")]
    [InlineData(unchecked((int)0x80000000), "read")]
    public void TheRightOfAnEntryIsItsStrongestName(int rights, string expected) =>
        Assert.Equal(expected, Doctor.RightName(rights));

    [Fact]
    public void AHomeTheBroadGroupsCanReadWarnsWithTheIcaclsLineThatFixesIt()
    {
        string home = WithProfile();
        DoctorEnvironment environment = Fake(home) with
        {
            ReadHomeAccess = _ => UserOnly(
                new("S-1-1-0", Allow: true, unchecked((int)0x80000000), Inherited: false),
                new("S-1-5-32-545", Allow: true, ReadAndExecute, Inherited: true),
                new("S-1-5-11", Allow: true, 0x10000000, Inherited: true),
                new("S-1-5-11", Allow: true, ReadAndExecute, Inherited: false)),
        };

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.False(check.Required);
        Assert.Equal("Everyone (read), Users (read, inherited), Authenticated Users (full control) can read it: every account on this machine can read the profiles, the plan records and the logs", check.Detail);
        Assert.Equal(
            $"icacls \"{home}\" /inheritance:r /grant:r \"CONTOSO\\alice:(OI)(CI)F\" \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /remove:g *S-1-1-0 *S-1-5-32-545 *S-1-5-11",
            check.Hint);
    }

    [Fact]
    public void TheHomeAccessIsSkippedOffWindowsAndWithoutAHomeAndWarnsWhenUnreadable()
    {
        bool read = false;
        DoctorEnvironment environment = Fake(WithProfile()) with { ReadHomeAccess = _ => { read = true; return null; } };

        Assert.Equal(CheckStatus.Skipped, Doctor.CheckHomeAccess(environment with { IsWindows = false }).Status);
        Assert.Equal(CheckStatus.Skipped, Doctor.CheckHomeAccess(environment with { Home = ProfileStoreTests.TempHome() }).Status);
        Assert.False(read);

        DoctorCheck unreadable = Doctor.CheckHomeAccess(environment);
        Assert.Equal(CheckStatus.Warn, unreadable.Status);
        Assert.Equal($"icacls \"{environment.Home}\" shows it", unreadable.Hint);
    }

    [Theory]
    [InlineData("pwsh=7.6.9\nmodule=3.10.0\n", CheckStatus.Pass, null)]
    [InlineData("pwsh=7.6.0-preview.4\nmodule=\n", CheckStatus.Pass, null)]
    [InlineData(null, CheckStatus.Warn, "pwsh -NoProfile")]
    [InlineData("something else\n", CheckStatus.Warn, "pwsh -NoProfile")]
    [InlineData("pwsh=7.4.6\nmodule=3.10.0\n", CheckStatus.Fail, "winget upgrade Microsoft.PowerShell")]
    public void Pwsh(string? output, CheckStatus status, string? hint)
    {
        DoctorCheck check = Doctor.CheckPwsh(@"D:\tools\pwsh\pwsh.exe", PwshAnswer.Parse(output));

        Assert.Equal(status, check.Status);
        Assert.True(check.Required);
        AssertHint(hint, check);
    }

    [Fact]
    public void PwshMissingFailsWithWhatToInstall()
    {
        DoctorCheck check = Doctor.CheckPwsh(null, null);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("winget install Microsoft.PowerShell", check.Hint);
    }

    [Theory]
    [InlineData("pwsh=7.6.9\nmodule=3.10.0\n", CheckStatus.Pass, null)]
    [InlineData("pwsh=7.6.9\r\nmodule=3.11.1\r\n", CheckStatus.Pass, null)]
    [InlineData("pwsh=7.6.9\nmodule=3.9.0\n", CheckStatus.Warn, "Update-Module ExchangeOnlineManagement")]
    [InlineData("pwsh=7.6.9\nmodule=\n", CheckStatus.Fail, "Install-Module ExchangeOnlineManagement -Scope CurrentUser")]
    [InlineData(null, CheckStatus.Skipped, null)]
    public void ExchangeOnlineModule(string? output, CheckStatus status, string? hint)
    {
        DoctorCheck check = Doctor.CheckExchangeModule(@"D:\tools\pwsh\pwsh.exe", PwshAnswer.Parse(output));

        Assert.Equal(status, check.Status);
        Assert.False(check.Required);
        AssertHint(hint, check);
    }

    [Fact]
    public void TheModuleIsSkippedWithoutPwsh() =>
        Assert.Equal(CheckStatus.Skipped, Doctor.CheckExchangeModule(null, null).Status);

    [Fact]
    public void TheSidecarScriptAgainstTheBuild()
    {
        Assert.Equal(CheckStatus.Pass, Doctor.CheckSidecarScript(Script, Script).Status);

        byte[] crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Script).Replace("\n", "\r\n", StringComparison.Ordinal));
        DoctorCheck lineEndings = Doctor.CheckSidecarScript(crlf, Script);
        Assert.Equal(CheckStatus.Warn, lineEndings.Status);
        Assert.Contains("line endings", lineEndings.Detail, StringComparison.Ordinal);

        byte[] changed = Encoding.UTF8.GetBytes("Invoke-Expression $evil\n");
        DoctorCheck tampered = Doctor.CheckSidecarScript(changed, Script);
        Assert.Equal(CheckStatus.Fail, tampered.Status);
        Assert.True(tampered.Required);
        Assert.Contains("changed after the build", tampered.Detail, StringComparison.Ordinal);
        Assert.Contains("reinstall", tampered.Hint, StringComparison.Ordinal);

        Assert.Equal(CheckStatus.Fail, Doctor.CheckSidecarScript(null, Script).Status);
        Assert.Equal(CheckStatus.Fail, Doctor.CheckSidecarScript(Script, null).Status);
    }

    [Fact]
    public void TheBuildCarriesTheScriptAndTheCopyNextToTheAssemblyMatchesIt()
    {
        byte[]? build = SidecarScript.ReadBuildCopy();

        Assert.NotNull(build);
        Assert.StartsWith("#Requires -Version 7.6", Encoding.UTF8.GetString(build), StringComparison.Ordinal);
        Assert.True(File.Exists(SidecarScript.InstalledPath), SidecarScript.InstalledPath);
        Assert.Equal(CheckStatus.Pass, Doctor.CheckSidecarScript(File.ReadAllBytes(SidecarScript.InstalledPath), build).Status);
    }

    [Theory]
    [InlineData("141.0.3537.71", null, CheckStatus.Pass, null)]
    [InlineData(null, "141.0.3537.71", CheckStatus.Pass, null)]
    [InlineData("109.0.1518.78", null, CheckStatus.Warn, "winget upgrade Microsoft.EdgeWebView2Runtime")]
    [InlineData("0.0.0.0", "", CheckStatus.Fail, "winget install Microsoft.EdgeWebView2Runtime")]
    [InlineData(null, null, CheckStatus.Fail, "winget install Microsoft.EdgeWebView2Runtime")]
    public void WebView2(string? perMachine, string? perUser, CheckStatus status, string? hint)
    {
        DoctorCheck check = Doctor.CheckWebView2(Fake(ProfileStoreTests.TempHome(), webView2: [perMachine, perUser]));

        Assert.Equal(status, check.Status);
        Assert.False(check.Required);
        AssertHint(hint, check);
    }

    [Theory]
    [InlineData(new[] { "8.0.11", "10.0.4" }, CheckStatus.Pass, "10.0.4", null)]
    [InlineData(new[] { "10.0.5" }, CheckStatus.Pass, "10.0.5", null)]
    [InlineData(new[] { "10.0.1", "10.0.0" }, CheckStatus.Warn, "10.0.1 is older than the .NET Runtime 10.0.4", "winget upgrade Microsoft.DotNet.DesktopRuntime.10")]
    [InlineData(new[] { "8.0.11", "9.0.6" }, CheckStatus.Fail, "(installed: 8.0.11, 9.0.6)", "winget install Microsoft.DotNet.DesktopRuntime.10")]
    [InlineData(new string[0], CheckStatus.Fail, "the .NET Desktop Runtime 10 is not installed", "winget install Microsoft.DotNet.DesktopRuntime.10")]
    public void TheDesktopRuntimeOfTheWindow(string[] installed, CheckStatus status, string detail, string? hint)
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with { ReadDesktopRuntimeVersions = () => installed };

        DoctorCheck check = Doctor.CheckDesktopRuntime(environment);

        Assert.Equal("dotnet-desktop-runtime", check.Name);
        Assert.Equal(status, check.Status);
        Assert.Contains(detail, check.Detail, StringComparison.Ordinal);
        Assert.False(check.Required);
        AssertHint(hint, check);
    }

    [Fact]
    public void ExesSignedByTheReleaseCertificatePass()
    {
        DoctorCheck check = Doctor.CheckSignature(Fake(ProfileStoreTests.TempHome()));

        Assert.Equal(new DoctorCheck("signature", CheckStatus.Pass, "3 file(s) signed by CN=Contoso Release Signing, the release's certificate", Required: false), check);
    }

    [Fact]
    public void ATrustedCertificateThatIsNotTheReleasesWarnsWithItsSignerAndThumbprint()
    {
        // Task 093: a machine that trusts a wrong certificate does not see a pass on a replaced file.
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () =>
            [
                Signed(SignatureState.Valid)[0],
                new("m365-aegis.dll", SignatureState.Valid, "CN=Fabrikam Code Signing", 0, OtherThumbprint),
                new("M365Aegis.Core.dll", SignatureState.Valid, "CN=Fabrikam Code Signing", 0),
                new("m365-aegis-gui.exe", SignatureState.Valid, null, 0),
            ],
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal(
            $"m365-aegis.dll, M365Aegis.Core.dll, m365-aegis-gui.exe: signed by CN=Fabrikam Code Signing ({OtherThumbprint}); CN=Fabrikam Code Signing (no thumbprint read); an unreadable certificate, not the release's certificate (thumbprint {Authenticode.ReleaseThumbprint})",
            check.Detail);
        Assert.Contains("releases page", check.Hint, StringComparison.Ordinal);
        Assert.False(check.Required);
    }

    [Fact]
    public void TheTestSeamPinsAnotherCertificateAndTheRowSaysSo()
    {
        // The signing tests pin their throwaway certificate through the variable; spaces and the case do not matter.
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () => Signed(SignatureState.Valid, OtherThumbprint),
            PinOverride = $" {OtherThumbprint[..8]} {OtherThumbprint[8..].ToLowerInvariant()} ",
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Equal("3 file(s) signed by CN=Contoso Release Signing, the certificate M365AEGIS_TEST_RELEASE_THUMBPRINT pins", check.Detail);
        Assert.Equal(CheckStatus.Warn, Doctor.CheckSignature(environment with { PinOverride = " " }).Status);
        Assert.Equal(CheckStatus.Pass, Doctor.CheckSignature(environment with { PinOverride = " ", ReadSignatures = () => Signed(SignatureState.Valid) }).Status);
    }

    [Fact]
    public void AnUntrustedCertificateThatIsNotTheReleasesIsNotTheOneToTrust()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () => [new("m365-aegis.exe", SignatureState.Untrusted, "CN=Fabrikam Code Signing", Authenticode.UntrustedRoot, OtherThumbprint)],
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("m365-aegis.exe (0x800B0109): signed by CN=Fabrikam Code Signing, which this machine does not trust", check.Detail);
        Assert.Equal($"the signer is not the release's certificate (thumbprint {Authenticode.ReleaseThumbprint}): unzip the release from the releases page again", check.Hint);
    }

    [Fact]
    public void AnUnsignedFileWarnsAndNamesIt()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () => [.. Signed(SignatureState.Valid).Take(2), new("m365-aegis.dll", SignatureState.NotSigned, null, Authenticode.NoSignature)],
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("m365-aegis.dll: not signed (a build from source, or an unsigned zip)", check.Detail);
        Assert.Contains("releases page", check.Hint, StringComparison.Ordinal);
        Assert.False(check.Required);
    }

    [Fact]
    public void ACertificateThisMachineDoesNotTrustWarnsWithTheSignerAndTheReason()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () =>
            [
                new("m365-aegis.exe", SignatureState.Untrusted, "CN=Contoso Release Signing", Authenticode.UntrustedRoot, Authenticode.ReleaseThumbprint),
                new("m365-aegis.dll", SignatureState.Untrusted, null, Authenticode.UntrustedRoot, Authenticode.ReleaseThumbprint),
            ],
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal(
            "m365-aegis.exe (0x800B0109), m365-aegis.dll (0x800B0109): signed by CN=Contoso Release Signing; an unreadable certificate, which this machine does not trust",
            check.Detail);
        Assert.Contains("Check the download", check.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignatureThatDoesNotHoldFailsAndComesBeforeTheWarnings()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with
        {
            ReadSignatures = () =>
            [
                new("m365-aegis.exe", SignatureState.Broken, "CN=Contoso Release Signing", Authenticode.BadDigest),
                new("m365-aegis.dll", SignatureState.NotSigned, null, Authenticode.NoSignature),
            ],
        };

        DoctorCheck check = Doctor.CheckSignature(environment);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("m365-aegis.exe (0x80096010): the signature does not hold, the file changed after it was signed", check.Detail);
        Assert.Contains("reinstall", check.Hint, StringComparison.Ordinal);
        Assert.False(check.Required);
    }

    [Fact]
    public void TheSignatureIsSkippedOffWindowsAndWithoutAProductFile()
    {
        Assert.Equal(CheckStatus.Skipped, Doctor.CheckSignature(Fake(ProfileStoreTests.TempHome()) with { IsWindows = false }).Status);

        DoctorCheck none = Doctor.CheckSignature(Fake(ProfileStoreTests.TempHome()) with { ReadSignatures = () => [] });
        Assert.Equal(CheckStatus.Skipped, none.Status);
        Assert.Contains("no m365-aegis exe", none.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WebView2AndTheDeviceAreSkippedOffWindows()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with { IsWindows = false };

        Assert.Equal(CheckStatus.Skipped, Doctor.CheckWebView2(environment).Status);
        Assert.Equal(CheckStatus.Skipped, Doctor.CheckDesktopRuntime(environment).Status);
        Assert.Equal(CheckStatus.Skipped, Doctor.CheckDevice(environment).Status);
    }

    [Fact]
    public void Config()
    {
        DoctorCheck pass = Doctor.CheckConfig(WithProfile());
        Assert.Equal(CheckStatus.Pass, pass.Status);
        Assert.Contains("default 'work'", pass.Detail, StringComparison.Ordinal);

        DoctorCheck missing = Doctor.CheckConfig(ProfileStoreTests.TempHome());
        Assert.Equal(CheckStatus.Warn, missing.Status);
        Assert.Contains("login --tenant", missing.Hint, StringComparison.Ordinal);

        string twoWithoutDefault = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(twoWithoutDefault);
        File.WriteAllText(Path.Combine(twoWithoutDefault, "config.json"), """
            {"profiles":{"work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"},
                         "lab":{"tenant":"fabrikam.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@fabrikam.com"}}}
            """);
        DoctorCheck noDefault = Doctor.CheckConfig(twoWithoutDefault);
        Assert.Equal(CheckStatus.Warn, noDefault.Status);
        Assert.Contains("profile use", noDefault.Hint, StringComparison.Ordinal);

        string broken = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "config.json"), "{ not json");
        DoctorCheck fail = Doctor.CheckConfig(broken);
        Assert.Equal(CheckStatus.Fail, fail.Status);
        Assert.False(fail.Required);
        Assert.NotNull(fail.Hint);
    }

    [Theory]
    [InlineData("dsregcmd-entra-joined.txt", CheckStatus.Pass, "primary refresh token")]
    [InlineData("dsregcmd-registered.txt", CheckStatus.Warn, "registered, not Entra-joined")]
    [InlineData("dsregcmd-neither.txt", CheckStatus.Fail, "neither Entra-joined nor registered")]
    public void Device(string fixture, CheckStatus status, string detail)
    {
        DoctorCheck check = Doctor.CheckDevice(Fake(ProfileStoreTests.TempHome(), deviceState: Fixture(fixture)));

        Assert.Equal(status, check.Status);
        Assert.False(check.Required);
        Assert.Contains(detail, check.Detail, StringComparison.Ordinal);
        if (status == CheckStatus.Fail)
        {
            Assert.Contains("Access work or school", check.Hint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEntraJoinedMachineWithoutAPrimaryRefreshTokenIsAWarning()
    {
        string output = Fixture("dsregcmd-entra-joined.txt").Replace("AzureAdPrt : YES", "AzureAdPrt : NO", StringComparison.Ordinal);

        DoctorCheck check = Doctor.CheckDevice(Fake(ProfileStoreTests.TempHome(), deviceState: output));

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.NotNull(check.Hint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dsregcmd: the command failed\n")]
    public void AnUnreadableDeviceStateIsAWarning(string? output) =>
        Assert.Equal(CheckStatus.Warn, Doctor.CheckDevice(Fake(ProfileStoreTests.TempHome(), deviceState: output)).Status);

    [Fact]
    public void TheDeviceDetailNeverNamesTheTenant()
    {
        DoctorCheck check = Doctor.CheckDevice(Fake(ProfileStoreTests.TempHome(), deviceState: Fixture("dsregcmd-entra-joined.txt")));

        Assert.DoesNotContain("00000000", check.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CONTOSO", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignIn()
    {
        (DoctorCheck pass, SignInInfo? read) = await Doctor.CheckSignInAsync(Fake(WithProfile()), CancellationToken.None);
        Assert.Equal(CheckStatus.Pass, pass.Status);
        Assert.NotNull(read);
        Assert.StartsWith("alice@contoso.com: graph token until 2026-10-29 15:00 +01:00, exchange token until", pass.Detail, StringComparison.Ordinal);

        (DoctorCheck partial, _) = await Doctor.CheckSignInAsync(Fake(WithProfile(), signIn: (_, _) => Task.FromResult(AuthSamples.NoExchangeToken)), CancellationToken.None);
        Assert.Equal(CheckStatus.Warn, partial.Status);
        Assert.Contains("no exchange token (consent_required", partial.Detail, StringComparison.Ordinal);

        (DoctorCheck failed, SignInInfo? none) = await Doctor.CheckSignInAsync(
            Fake(WithProfile(), signIn: (_, _) => throw new AegisException(FailureKind.Auth, "no silent sign-in for alice@contoso.com", "run `m365-aegis login`")),
            CancellationToken.None);
        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.Null(none);
        Assert.False(failed.Required);
        Assert.Equal("run `m365-aegis login`", failed.Hint);
    }

    [Fact]
    public async Task WithoutAProfileTheSignInIsAWarningAndNeverReachesTheBroker()
    {
        bool called = false;
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome(), signIn: (_, _) => { called = true; return Task.FromResult(AuthSamples.SignedIn); });

        (DoctorCheck check, SignInInfo? info) = await Doctor.CheckSignInAsync(environment, CancellationToken.None);

        Assert.False(called);
        Assert.Null(info);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("login --tenant", check.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsentPassesWhenEveryPermissionOfTheTableIsThere()
    {
        DoctorCheck check = Doctor.CheckConsent(AuthSamples.EveryPermission);

        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.False(check.Required);
        Assert.Null(check.Hint);
        Assert.Equal(
            "every permission of the setup guide's table is consented: User.Read, GroupMember.Read.All, User.ReadBasic.All, Team.ReadBasic.All, " +
            "Sites.Read.All, AuditLog.Read.All, Application.Read.All, ExchangeMessageTrace.Read.All, Reports.Read.All, GroupMember.ReadWrite.All, " +
            "Group.ReadWrite.All (Microsoft Graph) and Exchange.Manage, Exchange.ManageV2 (Exchange Online)",
            check.Detail);
    }

    [Fact]
    public void ConsentReadsThePermissionsWhateverTheirCase()
    {
        SignInInfo lower = AuthSamples.WithScopes(
            [.. AuthSamples.EveryPermission.Tokens[0].Scopes.Select(scope => scope.ToLowerInvariant())], ["exchange.manage", "EXCHANGE.MANAGEV2"]);

        Assert.Equal(CheckStatus.Pass, Doctor.CheckConsent(lower).Status);
    }

    [Fact]
    public void AMissingPermissionWarnsWithTheRowsOfTheTableAndTheConsentAsTheFix()
    {
        SignInInfo info = AuthSamples.WithScopes(
            [.. AuthSamples.EveryPermission.Tokens[0].Scopes.Where(scope => scope != "AuditLog.Read.All")], ["Exchange.Manage", "Exchange.ManageV2"]);

        DoctorCheck check = Doctor.CheckConsent(info);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.False(check.Required);
        Assert.Equal("not consented: signin list, audit list (AuditLog.Read.All)", check.Detail);
        Assert.Equal("an administrator consents to AuditLog.Read.All (Microsoft Graph) in the portal (the setup guide's Permissions table), then `m365-aegis login`", check.Hint);
    }

    [Fact]
    public void AMissingExchangePermissionNamesEveryRowThatNeedsIt()
    {
        SignInInfo info = AuthSamples.WithScopes(AuthSamples.EveryPermission.Tokens[0].Scopes, ["Exchange.Manage"]);

        DoctorCheck check = Doctor.CheckConsent(info);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal(
            "not consented: mailbox list, mailbox access, mailbox forwarders, mailbox folder-permissions, mailbox group-members, mailbox organization, " +
            "mailbox size <mailbox>, the window's mailbox cards (Exchange.ManageV2); " +
            "the same four writes on a distribution group or a mail-enabled security group (Exchange.ManageV2); " +
            "mailbox access grant, mailbox access revoke, mailbox forwarding set, mailbox forwarding clear, the window's Mailbox rights and Mailbox forwarding cards (Exchange.ManageV2); " +
            "snapshot take (the mailboxes, their rights, calendars and forwarding, the distribution lists' owners) (Exchange.ManageV2)",
            check.Detail);
        Assert.Equal("an administrator consents to Exchange.ManageV2 (Exchange Online) in the portal (the setup guide's Permissions table), then `m365-aegis login`", check.Hint);
    }

    [Fact]
    public void AResourceWithoutATokenIsUnreadAndTheOtherOneIsStillChecked()
    {
        // The Graph token carries User.Read only; the Exchange Online token is missing (its consent, the sign-in row says).
        DoctorCheck check = Doctor.CheckConsent(AuthSamples.NoExchangeToken);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.StartsWith("no exchange token, so its permissions are not read (the sign-in row says why); not consented: group members, group owners, group owners-missing (GroupMember.Read.All, User.ReadBasic.All); team list (Team.ReadBasic.All); ", check.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Exchange.Manage", check.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("User.Read)", check.Detail, StringComparison.Ordinal);
        Assert.StartsWith("an administrator consents to GroupMember.Read.All, User.ReadBasic.All, Team.ReadBasic.All, ", check.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain("Exchange Online", check.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void AResourceWithoutATokenAloneWarnsWithoutAFixOfItsOwn()
    {
        SignInInfo info = AuthSamples.EveryPermission with
        {
            Tokens = [AuthSamples.EveryPermission.Tokens[0], ResourceToken.Failed(Resource.Exchange, "consent_required")],
        };

        DoctorCheck check = Doctor.CheckConsent(info);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("no exchange token, so its permissions are not read (the sign-in row says why)", check.Detail);
        Assert.Null(check.Hint);
    }

    [Fact]
    public void ConsentIsSkippedWhenNoTokenWasRead()
    {
        DoctorCheck none = Doctor.CheckConsent(null);
        DoctorCheck failed = Doctor.CheckConsent(AuthSamples.SignedIn with
        {
            Tokens = [ResourceToken.Failed(Resource.Graph, "interaction_required"), ResourceToken.Failed(Resource.Exchange, "interaction_required")],
        });

        Assert.All([none, failed], check =>
        {
            Assert.Equal(CheckStatus.Skipped, check.Status);
            Assert.Equal("no token to read the permissions from; the sign-in row says why", check.Detail);
        });
    }

    [Fact]
    public async Task TheDoctorReadsTheConsentOfTheTokensTheSignInHandsOut()
    {
        // The scripted broker's tokens: Graph with User.Read and GroupMember.ReadWrite.All, Exchange Online with
        // Exchange.Manage under its resource prefix, which the sign-in strips as whoami shows it.
        string home = WithProfile();
        var auth = new AuthService(home, _ => new MarkedTokenBroker(MarkedTokenBroker.NewMarker()));
        DoctorEnvironment environment = Fake(home, signIn: auth.WhoAmIAsync);

        DoctorReport report = await Doctor.RunAsync(environment, offline: false, CancellationToken.None);

        DoctorCheck check = Assert.Single(report.Checks, check => check.Name == "consent");
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("; mailbox list, mailbox access, ", check.Detail, StringComparison.Ordinal);
        Assert.Contains(", the window's mailbox cards (Exchange.ManageV2, GroupMember.Read.All); ", check.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("login, whoami", check.Detail, StringComparison.Ordinal);
        Assert.Equal(
            "an administrator consents to GroupMember.Read.All, User.ReadBasic.All, Team.ReadBasic.All, Sites.Read.All, AuditLog.Read.All, " +
            "Application.Read.All, ExchangeMessageTrace.Read.All, Reports.Read.All, Group.ReadWrite.All (Microsoft Graph) and Exchange.ManageV2 " +
            "(Exchange Online) in the portal (the setup guide's Permissions table), then `m365-aegis login`",
            check.Hint);
        Assert.True(report.Healthy);
    }

    [Theory]
    [InlineData(200, 80, null, CheckStatus.Pass, null)]
    [InlineData(405, 120, null, CheckStatus.Pass, null)]
    [InlineData(407, 90, null, CheckStatus.Warn, "proxy")]
    [InlineData(200, 4200, null, CheckStatus.Warn, "network path")]
    [InlineData(null, 10000, "No such host is known.", CheckStatus.Fail, "firewall")]
    public void Endpoint(int? status, int milliseconds, string? error, CheckStatus expected, string? hint)
    {
        var answer = new EndpointAnswer(status, TimeSpan.FromMilliseconds(milliseconds), error);

        DoctorCheck check = Doctor.CheckEndpoint("endpoint-graph", new Uri("https://graph.microsoft.com/v1.0/"), answer);

        Assert.Equal(expected, check.Status);
        Assert.True(check.Required);
        Assert.StartsWith("graph.microsoft.com: ", check.Detail, StringComparison.Ordinal);
        if (hint is null)
        {
            Assert.Null(check.Hint);
        }
        else
        {
            Assert.Contains(hint, check.Hint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnUnreachableEndpointFailsTheReportWithTen()
    {
        DoctorEnvironment environment = Fake(WithProfile()) with
        {
            Probe = (uri, _) => Task.FromResult(uri.Host == "outlook.office365.com"
                ? new EndpointAnswer(null, TimeSpan.FromSeconds(10), "timed out")
                : new EndpointAnswer(200, TimeSpan.FromMilliseconds(50), null)),
        };

        DoctorReport report = await Doctor.RunAsync(environment, offline: false, CancellationToken.None);

        Assert.False(report.Healthy);
        Assert.Equal(ExitCode.DoctorFailed, report.ToResult().ExitCode);
        Assert.Equal(CheckStatus.Fail, Assert.Single(report.Checks, check => check.Name == "endpoint-exchange").Status);
    }

    [Fact]
    public async Task AFailureOfACheckThatIsNotRequiredKeepsTheReportHealthy()
    {
        DoctorEnvironment environment = Fake(WithProfile(), webView2: [null, null], deviceState: Fixture("dsregcmd-neither.txt"));

        DoctorReport report = await Doctor.RunAsync(environment, offline: true, CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Equal(2, report.Checks.Count(check => check.Status == CheckStatus.Fail));
    }

    [Fact]
    public async Task ThisMachineOfflineGivesAReportWithEveryCheck()
    {
        // No assumption about the machine: the checks run and each has an outcome; nothing touches the network.
        DoctorReport report = await Doctor.RunAsync(ProfileStoreTests.TempHome(), null, offline: true, CancellationToken.None);

        Assert.Equal(16, report.Checks.Count);
        Assert.Equal(CheckStatus.Skipped, Assert.Single(report.Checks, check => check.Name == "home-access").Status);
        DoctorCheck pwsh = Assert.Single(report.Checks, check => check.Name == "pwsh");
        string? found = Doctor.FindPwsh();
        Assert.Equal(found is null ? CheckStatus.Fail : pwsh.Status, pwsh.Status);
        Assert.NotEqual(CheckStatus.Skipped, pwsh.Status);
    }

    [Fact]
    public void ThePwshScriptAsksForBothVersionsInOneCall()
    {
        Assert.Contains("'pwsh='", Doctor.PwshScript, StringComparison.Ordinal);
        Assert.Contains("'module='", Doctor.PwshScript, StringComparison.Ordinal);
        Assert.Contains("M365AEGIS_EXO_MODULE_ROOT", Doctor.PwshScript, StringComparison.Ordinal);
    }

    private static void AssertHint(string? expected, DoctorCheck check)
    {
        if (expected is null)
        {
            Assert.Null(check.Hint);
        }
        else
        {
            Assert.Contains(expected, check.Hint, StringComparison.Ordinal);
        }
    }

    /// <summary>A home folder with one profile, the default.</summary>
    [Fact]
    public void PwshIsTheFirstFolderOfPathThatHoldsIt()
    {
        string empty = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;
        string holder = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;
        File.WriteAllText(Path.Combine(holder, "pwsh-test"), "");
        string path = string.Join(Path.PathSeparator, empty, $" {holder} ", "");

        Assert.Equal(Path.Combine(holder, "pwsh-test"), Doctor.FindPwsh(path, "pwsh-test"));
        Assert.Null(Doctor.FindPwsh(empty, "pwsh-test"));
        Assert.Null(Doctor.FindPwsh(null, "pwsh-test"));
        Assert.Null(Doctor.FindPwsh("", "pwsh-test"));
    }

    [Fact]
    public void AProcessAnswersWithItsOutputAndAMissingOrSlowOneWithNothing()
    {
        string pwsh = SidecarFixtures.Pwsh;

        Assert.Equal("hello", DoctorEnvironment.RunProcess(pwsh, ["-NoProfile", "-NonInteractive", "-Command", "'hello'"], TimeSpan.FromSeconds(30))?.Trim());
        Assert.Null(DoctorEnvironment.RunProcess(Path.Combine(Path.GetTempPath(), "m365-aegis-no-such-program"), [], TimeSpan.FromSeconds(5)));
        Assert.Null(DoctorEnvironment.RunProcess(pwsh, ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void AMissingFileReadsAsNothing()
    {
        string file = Path.Combine(Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName, "script.ps1");

        Assert.Null(DoctorEnvironment.ReadIfExists(file));
        File.WriteAllBytes(file, Script);
        Assert.Equal(Script, DoctorEnvironment.ReadIfExists(file));
    }

    [Fact]
    public void AnUnreadableRuntimeVersionIsTakenAsTen()
    {
        DoctorEnvironment environment = Fake(ProfileStoreTests.TempHome()) with { RuntimeVersion = "unknown" };

        DoctorCheck installed = Doctor.CheckDesktopRuntime(environment);
        DoctorCheck missing = Doctor.CheckDesktopRuntime(environment with { ReadDesktopRuntimeVersions = () => [] });

        Assert.Equal(CheckStatus.Pass, installed.Status);
        Assert.Equal("10.0.4", installed.Detail);
        Assert.Equal(CheckStatus.Fail, missing.Status);
        Assert.DoesNotContain("(installed:", missing.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfigWithoutAProfileWarns()
    {
        string home = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), """{"profiles":{}}""");

        DoctorCheck check = Doctor.CheckConfig(home);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.EndsWith("holds no profile", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrokerThatDoesNotAnswerFailsTheSignIn()
    {
        DoctorEnvironment environment = Fake(WithProfile(), signIn: (_, _) =>
            Task.FromException<SignInInfo>(new BrokerException(BrokerErrorKind.Other, "wam_provider_error_0", "the broker did not answer")));

        (DoctorCheck check, SignInInfo? info) = await Doctor.CheckSignInAsync(environment, CancellationToken.None);

        Assert.Null(info);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("wam_provider_error_0", check.Detail, StringComparison.Ordinal);
        Assert.Equal("run `m365-aegis login`", check.Hint);
    }

    [Fact]
    public void AnEndpointThatDidNotAnswerWithoutAReasonSaysSo()
    {
        DoctorCheck check = Doctor.CheckEndpoint("graph", new Uri("https://graph.microsoft.com/"), new EndpointAnswer(null, TimeSpan.FromSeconds(10), null));

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("(no reason given)", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownResourceHasNoName() => Assert.Equal("?", Doctor.ResourceName((Resource)99));

    private static string WithProfile()
    {
        string home = ProfileStoreTests.TempHome();
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        return home;
    }

    /// <summary>The access list a home the product created has (task 075), with the given entries added.</summary>
    private static HomeAccess UserOnly(params AccessEntry[] added) => new(
        @"CONTOSO\alice",
        "S-1-5-21-0-0-0-1001",
        [
            new("S-1-5-21-0-0-0-1001", Allow: true, FullControl, Inherited: false),
            new("S-1-5-18", Allow: true, FullControl, Inherited: false),
            new("S-1-5-32-544", Allow: true, FullControl, Inherited: false),
            .. added,
        ]);

    /// <summary>The three files of a command line folder, each in the given state, signed by the made-up publisher.</summary>
    private static FileSignature[] Signed(SignatureState state, string thumbprint = Authenticode.ReleaseThumbprint) =>
    [
        new("m365-aegis.exe", state, "CN=Contoso Release Signing", 0, thumbprint),
        new("m365-aegis.dll", state, "CN=Contoso Release Signing", 0, thumbprint),
        new("M365Aegis.Core.dll", state, "CN=Contoso Release Signing", 0, thumbprint),
    ];

    /// <summary>A made-up thumbprint of a certificate that is not the release's.</summary>
    private const string OtherThumbprint = "00000000000000000000000000000000000000AB";

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "doctor", fileName));

    /// <summary>A machine where everything is in place, with the given parts replaced.</summary>
    private static DoctorEnvironment Fake(
        string home,
        IReadOnlyList<string?>? webView2 = null,
        string? deviceState = "",
        Func<string?, CancellationToken, Task<SignInInfo>>? signIn = null) => new()
        {
            Home = home,
            IsWindows = true,
            RuntimeVersion = "10.0.4",
            FindPwsh = () => @"D:\tools\pwsh\pwsh.exe",
            RunPwsh = (_, _) => "pwsh=7.6.9\nmodule=3.10.0\n",
            ReadHomeAccess = _ => UserOnly(),
            ReadDesktopRuntimeVersions = () => ["8.0.11", "10.0.4"],
            ReadWebView2Versions = () => webView2 ?? ["141.0.3537.71", null],
            ReadDeviceState = () => deviceState == "" ? Fixture("dsregcmd-entra-joined.txt") : deviceState,
            ReadInstalledSidecar = () => Script,
            ReadBuildSidecar = () => Script,
            ReadSignatures = () => Signed(SignatureState.Valid),
            PinOverride = null,
            SignIn = signIn ?? ((_, _) => Task.FromResult(AuthSamples.EveryPermission)),
            Probe = (_, _) => Task.FromResult(new EndpointAnswer(200, TimeSpan.FromMilliseconds(80), null)),
        };
}
