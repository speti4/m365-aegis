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
    public void ViaNamesTheGroupANestedMemberCameThrough()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Fixtures, "--json", "group", "members", "Finance", "--via");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("via").GetBoolean());
        JsonElement dave = data.GetProperty("members").EnumerateArray().Single(member => member.GetProperty("displayName").GetString() == "Dave Example");
        Assert.Equal("Finance EU", dave.GetProperty("via")[0].GetProperty("displayName").GetString());
        // The resolution, the direct list's two pages, the transitive list and the one $batch.
        Assert.Equal(5, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Fact]
    public void TransitiveGroupMembersMarkTheNestedOnes()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "group", "members", "Finance", "--transitive");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("transitive").GetBoolean());
        Assert.Equal(6, data.GetProperty("members").GetArrayLength());
        Assert.Equal(2, data.GetProperty("members").EnumerateArray().Count(member => member.GetProperty("nested").GetBoolean()));
        Assert.Equal(4, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
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

    /// <summary>Graph's recorded answers and the fake module for the sidecar (the owners of task 076).</summary>
    private static Dictionary<string, string> Layers()
    {
        Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
        environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
        return environment;
    }

    [Fact]
    public void ADistributionGroupsOwnersAreItsManagedByThroughTheSidecar()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(), "--json", "group", "owners", "Sales");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("distribution", data.GetProperty("group").GetProperty("kind").GetString());
        Assert.True(data.GetProperty("fromExchange").GetBoolean());
        JsonElement alice = Assert.Single(data.GetProperty("owners").EnumerateArray());
        Assert.Equal(("user", "Alice Example"), (alice.GetProperty("type").GetString(), alice.GetProperty("displayName").GetString()));
        Assert.Equal("graph+pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void AMailEnabledSecurityGroupsOwnersTableHasNoGraphFooter()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(), "--table", "group", "owners", "00000000-0000-0000-0000-000000000035");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"user +Carol Example +carol@contoso\.com +", stdout);
        Assert.Matches("recipient +Organization Management +- +Organization Management", stdout);
        Assert.Contains("2 owner(s) of Finance Staff (mail-enabled security, 00000000-0000-0000-0000-000000000035)", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Graph keeps no owners", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void AMicrosoft365GroupsOwnersStillComeFromGraph()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(), "--json", "group", "owners", "Finance");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("fromExchange", out _));
        Assert.NotEqual(0, data.GetProperty("owners").GetArrayLength());
        Assert.Equal("graph", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void ASyncedGroupsOwnersKeepTheGraphFooter()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(), "--table", "group", "owners", "00000000-0000-0000-0000-00000000003a");

        Assert.Equal(0, exitCode);
        Assert.Contains("Sales Synced (distribution, synced, 00000000-0000-0000-0000-00000000003a)", stdout, StringComparison.Ordinal);
        Assert.Contains("Graph keeps no owners for this kind of group", stdout, StringComparison.Ordinal);
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

    [Fact]
    public void GroupsWithoutAnOwnerByDefaultAskGraphAlone()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(), "--json", "group", "owners-missing");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("managedInExchange", out _));
        Assert.Equal(3, data.GetProperty("leftOut").GetInt32());
        Assert.Equal("graph", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void AllTypesListsADistributionGroupOnlyWhenItsManagedByIsEmpty()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(), "--json", "group", "owners-missing", "--all-types");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        string?[] names = data.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("displayName").GetString()).ToArray();
        // All Staff's ManagedBy is empty; Sales Managers' names alice; Project Phoenix is Graph's answer.
        Assert.Equal(["All Staff", "Helpdesk Admins", "HR Synced", "Project Phoenix"], names);
        Assert.Equal(1, data.GetProperty("managedInExchange").GetInt32());
        Assert.Equal("graph+pwsh", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void AllTypesAtATerminalNamesWhatExchangeAndGraphJudged()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(), "--table", "group", "owners-missing", "--all-types");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"All Staff +distribution +allstaff@contoso\.com +", stdout);
        Assert.DoesNotContain("Sales Managers", stdout, StringComparison.Ordinal);
        Assert.Contains("4 group(s) without an owner", stdout, StringComparison.Ordinal);
        Assert.Contains("1 distribution or mail-enabled security group(s) left out: their ManagedBy in Exchange Online names an owner", stdout, StringComparison.Ordinal);
        Assert.Contains("1 synced group(s) as Graph lists them", stdout, StringComparison.Ordinal);
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
    public void ATraceByStatusIsFilteredByTheService()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(
            Fixtures, "--json", "trace", "--sender", "alice@contoso.com", "--until", "2026-10-20T12:00:00Z", "--status", "failed");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("service", data.GetProperty("filteredBy").GetString());
        Assert.Equal("failed", Assert.Single(data.GetProperty("messages").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ATraceFilteredHereTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        CommandResult<MessageTrace> result = await MessageTraceLookup.RunAsync(
            graph, new MessageTraceQuery("alice@contoso.com", null, null, null, Subject: "invoice"), now, TestContext.Current.CancellationToken);

        await Verify(Render(result, TraceTableView.Instance));
    }

    [Fact]
    public void TraceDetailsListTheStepsPerRecipient()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "trace", "details", "00000000-0000-0000-0000-000000000051");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement recipients = document.RootElement.GetProperty("data").GetProperty("recipients");
        Assert.Equal(2, recipients.GetArrayLength());
        Assert.Equal("Deliver", recipients[0].GetProperty("events")[2].GetProperty("event").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Fact]
    public void TraceDetailsOfAnUnknownMessageExitFour()
    {
        (int exitCode, _, string stderr) = CliTests.RunWith(Fixtures, "--table", "trace", "details", "00000000-0000-0000-0000-000000000059");

        Assert.Equal(4, exitCode);
        Assert.Contains("(not-found)", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATraceDetailsTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<MessageTraceDetails> result = await MessageTraceDetailsLookup.RunAsync(
            graph, new MessageTraceDetailsQuery("<00000000-0000-0000-0000-000000000052@contoso.com>", null), TestContext.Current.CancellationToken);

        await Verify(Render(result, TraceDetailsTableView.Instance));
    }

    [Fact]
    public async Task AGroupMembersTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: false, TestContext.Current.CancellationToken);

        await Verify(Render(result, GroupMembersTableView.Instance));
    }

    [Fact]
    public async Task ATransitiveGroupMembersTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: true, TestContext.Current.CancellationToken);

        await Verify(Render(result, GroupMembersTableView.Instance));
    }

    [Fact]
    public async Task AGroupMembersTableWithTheViaColumn()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: true, via: true, TestContext.Current.CancellationToken);

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
