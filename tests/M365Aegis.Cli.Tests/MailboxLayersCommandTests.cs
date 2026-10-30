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
/// The mailbox commands over the three layers (tasks 042 to 044): as child processes whose sidecar runs against the
/// fake module and whose Admin API and Graph answer from the recorded fixtures, and their tables in process.
/// </summary>
public sealed class MailboxLayersCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The fake module for the sidecar and the fixtures for the Admin API and Graph.</summary>
    private static Dictionary<string, string> Layers
    {
        get
        {
            Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
            environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
            return environment;
        }
    }

    [Fact]
    public void AccessNamesTheLayersItReadThrough()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers, "--json", "mailbox", "access", "finance");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("admin-api+pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        JsonElement onBehalf = document.RootElement.GetProperty("data").GetProperty("holders").EnumerateArray().Single(holder => holder.GetProperty("right").GetString() == "send-on-behalf");
        Assert.Equal("Bob Example", onBehalf.GetProperty("holder").GetString());
        Assert.Equal("bob@contoso.com", onBehalf.GetProperty("address").GetString());

        // Each grant names the layer that read it (task 063), the rows the window's access card shows.
        Assert.Equal("admin-api", document.RootElement.GetProperty("data").GetProperty("mailboxLayer").GetString());
        Assert.Equal(
            ["pwsh", "pwsh", "admin-api"],
            document.RootElement.GetProperty("data").GetProperty("holders").EnumerateArray().Select(holder => holder.GetProperty("layer").GetString()));
        Assert.Contains("00000000-0000-0000-0000-0000000000a1", document.RootElement.GetProperty("meta").GetProperty("requestIds").EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public void ExpandGroupsListsAGroupHoldersMembers()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers, "--json", "mailbox", "access", "reception", "--expand-groups");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement group = document.RootElement.GetProperty("data").GetProperty("holders").EnumerateArray().Single(holder => holder.GetProperty("group").GetBoolean());
        Assert.Equal("Finance Staff", group.GetProperty("holder").GetString());
        Assert.Equal(4, group.GetProperty("members").GetArrayLength());
        Assert.Equal("admin-api+pwsh+graph", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void TheReverseLookupWithExpandGroupsFindsAGrantThroughAGroup()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers, "--json", "mailbox", "access", "--user", "carol@contoso.com", "--expand-groups");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        JsonElement grant = Assert.Single(data.GetProperty("grants").EnumerateArray());
        Assert.Equal("Finance Staff", grant.GetProperty("via").GetProperty("displayName").GetString());
        Assert.Equal(2, data.GetProperty("groups").GetInt32());
    }

    [Theory]
    [InlineData("parallel", "--sidecars", "2")]
    [InlineData("piped")]
    [InlineData("parallel")]
    public void TheReverseLookupTakesAMethodAndIsParallelByDefault(string method, params string[] rest)
    {
        string[] args = method == "parallel" && rest.Length == 0
            ? ["--json", "mailbox", "access", "--user", "alice@contoso.com"]
            : ["--json", "mailbox", "access", "--user", "alice@contoso.com", "--method", method, .. rest];
        (int exitCode, string stdout, _) = CliTests.RunWith(MailboxCommandTests.FakeExchange, args);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(method, data.GetProperty("method").GetString());
        Assert.Equal(3, data.GetProperty("grants").GetArrayLength());
    }

    [Theory]
    [InlineData("mailbox", "access", "finance", "--method", "piped")]
    [InlineData("mailbox", "access", "--user", "alice@contoso.com", "--method", "identityless")]
    [InlineData("mailbox", "size", "--method", "fast")]
    [InlineData("mailbox", "size", "--method", "parallel", "--sidecars", "12")]
    public void AWrongMethodIsAUsageError(params string[] args)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(MailboxCommandTests.FakeExchange, ["--json", .. args]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void SizesFromTheUsageReport()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers, "--json", "mailbox", "size", "--top", "3");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("report", data.GetProperty("method").GetString());
        Assert.Equal("2026-10-28", data.GetProperty("reportDate").GetString());
        Assert.Equal(3, data.GetProperty("mailboxes").GetArrayLength());
        Assert.Equal("graph+admin-api", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Theory]
    [InlineData(0, "mailbox", "folder-permissions", "finance", "--folder", "Calendar")]
    [InlineData(0, "mailbox", "group-members", "sales")]
    [InlineData(0, "mailbox", "organization")]
    [InlineData(4, "mailbox", "folder-permissions", "finance", "--folder", "Missing")]
    [InlineData(4, "mailbox", "group-members", "nobody")]
    public void TheAdminApiReadsAnswerFromTheAdminApiAlone(int expected, params string[] args)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers, ["--json", .. args]);

        Assert.Equal(expected, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("admin-api", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void TheDefaultFolderIsTheCalendarInTheMailboxsLanguage()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers, "--json", "mailbox", "folder-permissions", "reception");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("Kalender", document.RootElement.GetProperty("data").GetProperty("folder").GetString());
        Assert.Equal("admin-api+pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public async Task TheLayeredMailboxTables()
    {
        await using var backends = new ExchangeBackends(
            SidecarFixtures.Session(),
            AdminApiConnection.Fixtures(GraphFixtures.Root),
            () => GraphFixtures.Client().Client);
        await using ExchangeSession sidecar = (ExchangeSession)backends.Sidecar;

        CommandResult<MailboxAccess> access = await MailboxLookups.AccessAsync(backends, "reception", new MailboxAccessOptions(ExpandGroups: true), Ct);
        CommandResult<UserAccess> user = await MailboxLookups.AccessForUserAsync(backends, "dave@contoso.com", new UserAccessOptions(ExpandGroups: true), Ct);
        CommandResult<MailboxSizes> sizes = await MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(), Ct);
        CommandResult<MailboxFolderPermissions> folder = await MailboxLookups.FolderPermissionsAsync(backends, "finance", null, Ct);
        CommandResult<DistributionGroupMembers> members = await MailboxLookups.DistributionGroupMembersAsync(backends, "sales", Ct);
        CommandResult<ExchangeOrganization> organization = await MailboxLookups.OrganizationAsync(backends, Ct);

        await Verify(
            Render(access, MailboxAccessTableView.Instance)
            + Render(user, UserAccessTableView.Instance)
            + Render(sizes, MailboxSizeTableView.Instance)
            + Render(folder, FolderPermissionsTableView.Instance)
            + Render(members, DistributionMembersTableView.Instance)
            + Render(organization, OrganizationTableView.Instance));
    }

    private static string Render<T>(CommandResult<T> result, ITableView<T> view)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.AdminApi), view, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
