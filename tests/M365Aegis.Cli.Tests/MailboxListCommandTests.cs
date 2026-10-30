using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>mailbox list</c> through the Admin API (task 048): as a child process whose Admin API answers from the recorded
/// fixtures, with the sidecar over the fake module left unused, and its table in process.
/// </summary>
public sealed class MailboxListCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(6)]
    [InlineData(2, "--shared")]
    public void TheListAnswersFromTheAdminApiAlone(int count, params string[] shared)
    {
        Dictionary<string, string> layers = MailboxCommandTests.FakeExchange;
        layers[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(layers, ["--json", "mailbox", "list", .. shared]);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("admin-api", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(count, document.RootElement.GetProperty("data").GetProperty("mailboxes").GetArrayLength());
        Assert.Contains("00000000-0000-0000-0000-0000000000a8", document.RootElement.GetProperty("meta").GetProperty("requestIds").EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public void WithoutAnAdminApiTheSidecarStillLists()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(MailboxCommandTests.FakeExchange, "--json", "mailbox", "list", "--shared");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("data").GetProperty("mailboxes").GetArrayLength());
    }

    [Fact]
    public async Task TheMailboxListTableFromTheAdminApi()
    {
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(sidecar, AdminApiConnection.Fixtures(GraphFixtures.Root));

        CommandResult<MailboxList> all = await MailboxLookups.ListAsync(backends, sharedOnly: false, Ct);
        CommandResult<MailboxList> shared = await MailboxLookups.ListAsync(backends, sharedOnly: true, Ct);

        Assert.Equal(0, sidecar.Starts);
        await Verify(Render(all) + Render(shared));
    }

    private static string Render(CommandResult<MailboxList> result)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.AdminApi), MailboxListTableView.Instance, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
