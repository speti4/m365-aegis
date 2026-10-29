using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The mailbox commands (tasks 015 and 016): as child processes whose sidecar runs against the fake module
/// (<see cref="ExchangeConnection.TestOrganizationVariable"/>, no sign-in), and their tables in process, byte for byte.
/// </summary>
public sealed class MailboxCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The environment of a child process whose sidecar uses the fake module.</summary>
    internal static Dictionary<string, string> FakeExchange => new()
    {
        [ExchangeConnection.TestOrganizationVariable] = SidecarFixtures.Organization,
        ["M365AEGIS_EXO_MODULE_ROOT"] = SidecarFixtures.ModuleRoot,
    };

    [Fact]
    public void MailboxListIsAnEnvelopeFromThePwshBackend()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(FakeExchange, "--json", "mailbox", "list", "--shared");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        JsonElement mailboxes = document.RootElement.GetProperty("data").GetProperty("mailboxes");
        Assert.Equal(2, mailboxes.GetArrayLength());
        Assert.Equal("finance@contoso.com", mailboxes[0].GetProperty("primarySmtpAddress").GetString());
    }

    [Fact]
    public void TheReverseLookupAtATerminalIsATable()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(FakeExchange, "--table", "mailbox", "access", "--user", "bob@contoso.com");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("mailbox", stdout, StringComparison.Ordinal);
        Assert.Contains("Send on Behalf", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("finance", "--user", "alice@contoso.com")]
    public void AccessWantsAMailboxOrAUserButNotBoth(params string[] rest)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(FakeExchange, ["--json", "mailbox", "access", .. rest]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("nobody", 4, "not-found")]
    [InlineData("broken", 13, "cmdlet-failed")]
    [InlineData("busy", 5, "throttled")]
    public void AFailedLookupExitsWithItsCode(string mailbox, int expected, string code)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(FakeExchange, "--json", "mailbox", "size", mailbox);

        Assert.Equal(expected, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal(code, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void WithoutPwshTheSidecarIsUnavailableWithElevenAndTheInstallHint()
    {
        Dictionary<string, string> environment = FakeExchange;
        environment["PATH"] = "";

        (int exitCode, string stdout, _) = CliTests.RunWith(environment, "--json", "mailbox", "list");

        Assert.Equal(11, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("sidecar-unavailable", error.GetProperty("code").GetString());
        Assert.Contains("winget install Microsoft.PowerShell", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAProfileTheMailboxCommandsAskForTheLogin()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(
            new() { ["M365AEGIS_HOME"] = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N")) },
            "--json", "mailbox", "forwarders");

        Assert.Equal(3, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("auth", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void TheProbeIsHiddenFromTheHelp()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith([], "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("mailbox", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("probe", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConnectMeasurementRunsFreshSidecarsPerVariant()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(FakeExchange, "--json", "probe", "exchange-measure", "--runs", "1", "--variants", "default,command-name");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("runs").GetArrayLength());
        Assert.Equal(["default", "command-name"], data.GetProperty("summary").EnumerateArray().Select(summary => summary.GetProperty("variant").GetString()));
        Assert.DoesNotContain(ExchangeConnection.TestToken, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMeasurementTable()
    {
        var report = new ConnectMeasurementReport(
            new MeasureEnvironment("7.6.9", "3.10.1", false, false, 0, false, ExoCmdlets.Allowed, ["Exchange.Manage", "Exchange.ManageV2"]),
            [
                new VariantSummary(ConnectVariant.Default, 2, 0, new Spread(4100, 4000, 4200), new Spread(470, 460, 480), new Spread(1060, 1050, 1070), new Spread(2300, 2200, 2400), new Spread(300, 290, 310), true),
                new VariantSummary(ConnectVariant.CommandName, 2, 1, new Spread(3000, 3000, 3000), new Spread(465, 465, 465), new Spread(1055, 1055, 1055), new Spread(1290, 1290, 1290), new Spread(180, 180, 180), true),
            ],
            [
                new ConnectRun(ConnectVariant.Default, 1, 4000, 460, 1050, 2200, 290, [], null),
                new ConnectRun(ConnectVariant.Default, 2, 4200, 480, 1070, 2400, 310, [], null),
                new ConnectRun(ConnectVariant.CommandName, 1, 3000, 465, 1055, 1290, 180, [], null),
                new ConnectRun(ConnectVariant.CommandName, 2, 900, 470, 0, 0, 0, [], "throttled: Exchange Online throttled the request: try again later"),
            ]);

        await Verify(Render(CommandResult.Success(report), ConnectMeasurementTableView.Instance));
    }

    [Fact]
    public async Task TheMailboxTables()
    {
        await using ExchangeSession exo = SidecarFixtures.Session();

        CommandResult<MailboxList> list = await MailboxLookups.ListAsync(exo, sharedOnly: false, Ct);
        CommandResult<MailboxAccess> access = await MailboxLookups.AccessAsync(exo, "finance", Ct);
        CommandResult<UserAccess> user = await MailboxLookups.AccessForUserAsync(exo, "alice@contoso.com", Ct);
        CommandResult<MailboxForwarders> forwarders = await MailboxLookups.ForwardersAsync(exo, Ct);
        CommandResult<MailboxSizes> sizes = await MailboxLookups.SizeAsync(exo, null, null, Ct);

        await Verify(
            Render(list, MailboxListTableView.Instance)
            + Render(access, MailboxAccessTableView.Instance)
            + Render(user, UserAccessTableView.Instance)
            + Render(forwarders, ForwardersTableView.Instance)
            + Render(sizes, MailboxSizeTableView.Instance));
    }

    [Fact]
    public async Task APartialReverseLookupKeepsItsTableAndNamesTheFailure()
    {
        var result = CommandResult.Failure(
            new AegisFailure(FailureKind.Partial, "1 mailbox permission list(s) could not be read; the answer lists the rest", "the data's `failed` list has the service's messages; run the command again for the rest"),
            new UserAccess("alice@contoso.com", "Alice Example", [new UserGrant(MailboxRight.FullAccess, new MailboxInfo("Finance", "finance@contoso.com", "SharedMailbox", null, null))], 6, ["fake: Server Busy"]));

        await Verify(Render(result, UserAccessTableView.Instance));
    }

    [Fact]
    public async Task TheProbeTables()
    {
        var connect = new ConnectProbeReport("Organization", "7.6.9", "3.10.1", 812, 950, 2400, 310, 2100, 280, 59.5, ["Exchange.Manage", "Exchange.ManageV2"]);
        var adminApi = new AdminApiProbeReport(
            true,
            ["Exchange.Manage", "Exchange.ManageV2"],
            [
                new AdminApiCall("OrganizationConfig", "UPN:", 200, 640, 1, ["@odata.context: string", "value: array[1]", "value[0].Name: string"], null, null, null),
                new AdminApiCall("Mailbox", "UPN:", 200, 720, 1, ["value: array[1]", "@odata.nextLink: string"], ["$skiptoken"], null, null),
            ]);

        await Verify(Render(CommandResult.Success(connect), ConnectProbeTableView.Instance) + Render(CommandResult.Success(adminApi), AdminApiProbeTableView.Instance));
    }

    private static string Render<T>(CommandResult<T> result, ITableView<T> view)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.Pwsh), view, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
