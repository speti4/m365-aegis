using System.Text;
using System.Text.RegularExpressions;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The sidecar from the core's side (task 015): a real pwsh child running the script of the build against the fake
/// ExchangeOnlineManagement module, driven through <see cref="SidecarClient"/> and <see cref="ExchangeSession"/>, on
/// Linux and Windows alike.
/// </summary>
public sealed partial class SidecarTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheScriptsAllowListIsTheCores()
    {
        string script = Encoding.UTF8.GetString(SidecarScript.ReadBuildCopy()!);
        Match block = AllowListBlock().Match(script);

        Assert.True(block.Success, "the script has no $script:AllowedCmdlets = @( … ) block");
        List<string> names = QuotedName().Matches(block.Groups[1].Value).Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(ExoCmdlets.Allowed, names);
    }

    [Fact]
    public async Task HelloNamesThePwshTheModuleAndTheAllowList()
    {
        await using SidecarClient client = await SidecarClient.StartAsync(SidecarFixtures.StartInfo(), Ct);

        Assert.StartsWith("7.", client.Hello.Pwsh, StringComparison.Ordinal);
        Assert.Equal("3.10.1", client.Hello.Module);
        Assert.Equal(ExoCmdlets.Allowed, client.Hello.AllowedCmdlets);
        Assert.True(client.Hello.StartMs > 0);
    }

    [Fact]
    public async Task ASessionConnectsWithTheTokenAndRunsAnAllowedCmdlet()
    {
        await using ExchangeSession session = SidecarFixtures.Session();

        ExoResult result = await session.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetMailbox) { Parameters = new Dictionary<string, object?> { ["Identity"] = "finance" }, Properties = ["PrimarySmtpAddress", "GrantSendOnBehalfTo"] },
            Ct);

        ExoObject mailbox = Assert.Single(result.Items);
        Assert.Equal("finance@contoso.com", mailbox.Text("PrimarySmtpAddress"));
        Assert.Equal(["Bob Example"], mailbox.Texts("GrantSendOnBehalfTo"));
        Assert.Equal(SidecarFixtures.Organization, session.Connection!.Organization);
        Assert.Equal(1, session.Starts);
        Assert.Equal(0, session.Renewals);
    }

    [Fact]
    public async Task ARefusedCmdletIsExitCodeTwelveAndNeverStartsTheSidecar()
    {
        await using ExchangeSession session = SidecarFixtures.Session();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            session.InvokeAsync(new ExoCommand("Remove-Mailbox") { Parameters = new Dictionary<string, object?> { ["Identity"] = "alice" } }, Ct));

        Assert.Equal(ExitCode.CmdletNotAllowed, exception.Failure.ExitCode);
        Assert.Equal("cmdlet-not-allowed", exception.Failure.Code);
        Assert.Equal(0, session.Starts);
    }

    [Fact]
    public async Task TheScriptRefusesACmdletOffItsListToo()
    {
        await using SidecarClient client = await SidecarClient.StartAsync(SidecarFixtures.StartInfo(), Ct);
        await client.ConnectAsync(new SidecarConnect("fake-token", SidecarFixtures.Organization, null), again: false, Ct);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            client.SendAsync("invoke", new Dictionary<string, object?> { ["cmdlet"] = "Remove-Mailbox" }, TimeSpan.FromSeconds(30), Ct));

        Assert.Equal(FailureKind.CmdletNotAllowed, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("broken", FailureKind.CmdletFailed, 13)]
    [InlineData("nobody", FailureKind.NotFound, 4)]
    [InlineData("busy", FailureKind.Throttled, 5)]
    public async Task AFailedCmdletIsTheFailureItsMessageNames(string identity, FailureKind kind, int exitCode)
    {
        await using ExchangeSession session = SidecarFixtures.Session();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            session.InvokeAsync(new ExoCommand(ExoCmdlets.GetMailbox) { Parameters = new Dictionary<string, object?> { ["Identity"] = identity } }, Ct));

        Assert.Equal(kind, exception.Failure.Kind);
        Assert.Equal(exitCode, (int)exception.Failure.ExitCode);
    }

    [Fact]
    public async Task ABulkRunKeepsItsObjectsAndListsTheFailedItems()
    {
        await using ExchangeSession session = SidecarFixtures.Session();

        ExoResult result = await session.InvokeAsync(new ExoCommand(ExoCmdlets.GetMailboxStatistics) { Input = ["alice", "nobody", "bob"] }, Ct);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains("couldn't be found", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenAboutToExpireIsRenewedWithAReconnect()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        int asked = 0;
        await using ExchangeSession session = SidecarFixtures.Session(
            token: _ =>
            {
                asked++;
                return Task.FromResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60), $"token-{asked}"));
            },
            clock: clock);
        var domains = new ExoCommand(ExoCmdlets.GetAcceptedDomain);

        await session.InvokeAsync(domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(50));
        await session.InvokeAsync(domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(6));
        ExoResult afterRenewal = await session.InvokeAsync(domains, Ct);

        Assert.Equal(2, asked);
        Assert.Equal(1, session.Renewals);
        Assert.Equal("token-2", session.Token!.AccessToken);
        Assert.Equal(2, afterRenewal.Items.Count);
        Assert.Equal(1, session.Starts);
    }

    [Fact]
    public async Task ADeadSidecarIsReplacedOnTheNextCmdlet()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        var domains = new ExoCommand(ExoCmdlets.GetAcceptedDomain);
        await session.InvokeAsync(domains, Ct);

        session.Client!.Kill();
        ExoResult result = await session.InvokeAsync(domains, Ct);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, session.Starts);
    }

    [Fact]
    public async Task AnExpiredTokenIsAnAuthFailure()
    {
        await using ExchangeSession session = SidecarFixtures.Session(token: _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1), "expired-token")));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => session.ConnectAsync(Ct));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.DoesNotContain("expired-token", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConnectTheServiceRefusesForAnotherReasonIsExitCodeEleven()
    {
        await using ExchangeSession session = SidecarFixtures.Session(target: new ExchangeTarget("fabrikam.onmicrosoft.com", null));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => session.ConnectAsync(Ct));

        Assert.Equal(ExitCode.SidecarUnavailable, exception.Failure.ExitCode);
        Assert.Contains("unknown organization", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConnectWithTheUserPrincipalNameOnlyIsAccepted()
    {
        await using ExchangeSession session = SidecarFixtures.Session(target: new ExchangeTarget(null, "alice@contoso.com"));

        SidecarConnected connected = await session.ConnectAsync(Ct);

        Assert.Null(connected.Organization);
        Assert.Equal("alice@contoso.com", connected.UserPrincipalName);
    }

    [Fact]
    public async Task AMissingModuleIsExitCodeElevenWithTheInstallCommand()
    {
        await using ExchangeSession session = SidecarFixtures.Session(moduleRoot: Path.Combine(Path.GetTempPath(), "m365-aegis-tests", "no-modules"));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => session.ConnectAsync(Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.Contains("Install-Module ExchangeOnlineManagement", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APwshThatDoesNotStartIsExitCodeEleven()
    {
        var start = new SidecarStartInfo(Path.Combine(Path.GetTempPath(), "m365-aegis-tests", "no-pwsh", "pwsh.exe"), SidecarScript.InstalledPath);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => SidecarClient.StartAsync(start, Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.Contains("doctor", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTokenNeverShowsInAString()
    {
        ExchangeToken token = SidecarFixtures.Token(DateTimeOffset.UtcNow, "secret-token-value");
        var connect = new SidecarConnect("secret-token-value", SidecarFixtures.Organization, null);

        Assert.DoesNotContain("secret-token-value", token.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-value", connect.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("organization", ExchangeConnectWith.Organization)]
    [InlineData(null, ExchangeConnectWith.Organization)]
    [InlineData("UPN", ExchangeConnectWith.UserPrincipalName)]
    [InlineData("both", ExchangeConnectWith.Both)]
    public void TheConnectVariantIsReadFromItsVariable(string? value, ExchangeConnectWith expected) =>
        Assert.Equal(expected, ExchangeConnection.ParseConnectWith(value));

    [GeneratedRegex(@"\$script:AllowedCmdlets = @\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex AllowListBlock();

    [GeneratedRegex("'([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedName();

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
