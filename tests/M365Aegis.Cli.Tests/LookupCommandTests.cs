using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The group, team, site and trace lookups: as child processes over the recorded answers, and their tables in
/// process, byte for byte.
/// </summary>
public sealed class LookupCommandTests
{
    private static readonly Dictionary<string, string> Fixtures = CallCommandTests.Fixtures;

    [Fact]
    public void GroupMembersIsAnEnvelopeWithTheGroupAndItsMembers()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Fixtures, "--json", "group", "members", "Finance");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("microsoft365", data.GetProperty("group").GetProperty("kind").GetString());
        Assert.Equal(4, data.GetProperty("members").GetArrayLength());
        Assert.Equal(3, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Fact]
    public void AnAmbiguousGroupNameExitsFourWithTheCandidates()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Fixtures, "--table", "group", "owners", "Fin");

        Assert.Equal(4, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("(not-found)", stderr, StringComparison.Ordinal);
        Assert.Contains("Finance EU (00000000-0000-0000-0000-000000000032)", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupsWithoutAnOwnerAtATerminalIsATable()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--table", "group", "owners-missing");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("name", stdout, StringComparison.Ordinal);
        Assert.Contains("Project Phoenix", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("All Staff", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("team", "list")]
    [InlineData("site", "list")]
    public void TheListsAnswerFromTheFixtures(string noun, string verb)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", noun, verb);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.True(document.RootElement.GetProperty("data").GetArrayLength() >= 2);
    }

    [Fact]
    public void ATraceWiderThanTenDaysIsRefusedWithAHint()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "trace", "--sender", "alice@contoso.com", "--days", "11");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("usage", error.GetProperty("code").GetString());
        Assert.Contains("at most 10 days", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("--until", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
        // Nothing was sent.
        Assert.Equal(0, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Theory]
    [InlineData("--days", "3")]
    [InlineData("--until", "yesterday")]
    public void ATraceWithoutAnAddressOrWithABadTimeIsAUsageError(string option, string value)
    {
        string[] args = option == "--until"
            ? ["--json", "trace", "--sender", "alice@contoso.com", option, value]
            : ["--json", "trace", option, value];

        (int exitCode, _, _) = CliTests.RunWith(Fixtures, args);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task AGroupMembersTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", TestContext.Current.CancellationToken);

        await Verify(Render(result, GroupMembersTableView.Instance));
    }

    [Fact]
    public async Task AGroupWithoutOwnersTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupOwners> result = await GroupLookups.OwnersAsync(graph, "Phoenix", TestContext.Current.CancellationToken);

        await Verify(Render(result, GroupOwnersTableView.Instance));
    }

    [Fact]
    public async Task AnOwnerlessGroupsTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(graph, allTypes: false, TestContext.Current.CancellationToken);

        await Verify(Render(result, OwnerlessGroupsTableView.Instance));
    }

    [Fact]
    public async Task ATeamAndASiteTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<IReadOnlyList<TeamInfo>> teams = await WorkspaceLookups.TeamsAsync(graph, TestContext.Current.CancellationToken);
        CommandResult<IReadOnlyList<SiteInfo>> sites = await WorkspaceLookups.SitesAsync(graph, null, TestContext.Current.CancellationToken);

        await Verify(Render(teams, TeamTableView.Instance) + Render(sites, SiteTableView.Instance));
    }

    [Fact]
    public async Task AMessageTraceTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        CommandResult<MessageTrace> result = await MessageTraceLookup.RunAsync(
            graph, new MessageTraceQuery("alice@contoso.com", null, null, null), now, TestContext.Current.CancellationToken);

        await Verify(Render(result, TraceTableView.Instance));
    }

    [Fact]
    public async Task AMessageTraceEnvelope()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        CommandResult<MessageTrace> result = await MessageTraceLookup.RunAsync(
            graph, new MessageTraceQuery("alice@contoso.com", null, null, null), now, TestContext.Current.CancellationToken);

        await Verify(EnvelopeJson.Serialize(result.ToEnvelope(new EnvelopeMeta(graph.RequestLog.Ids, 12, Backend.Graph))), extension: "json");
    }

    private static string Render<T>(CommandResult<T> result, ITableView<T> view)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.Graph), view, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
