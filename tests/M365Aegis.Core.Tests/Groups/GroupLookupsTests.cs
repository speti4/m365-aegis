using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>The group, team and site lookups over the recorded answers.</summary>
public sealed class GroupLookupsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AnExactNameResolvesWithAnAdvancedQuery()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        GroupInfo group = await GroupLookups.ResolveAsync(graph, "Finance", None);

        Assert.Equal(new GroupInfo("00000000-0000-0000-0000-000000000031", "Finance", GroupKind.Microsoft365, "finance@contoso.com", false), group);
        GraphHttpRequest request = Assert.Single(transport.Requests);
        Assert.Equal("eventual", request.Headers["ConsistencyLevel"]);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000031")]
    [InlineData("finance@contoso.com")]
    public async Task AnIdOrAnAddressResolvesToo(string argument)
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        GroupInfo group = await GroupLookups.ResolveAsync(graph, argument, None);

        Assert.Equal("Finance", group.DisplayName);
    }

    [Fact]
    public async Task WithoutAnExactNameTheOneSearchHitResolves()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        GroupInfo group = await GroupLookups.ResolveAsync(graph, "Phoenix", None);

        Assert.Equal("Project Phoenix", group.DisplayName);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Contains("$search=", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmbiguousNameIsNotFoundWithTheCandidatesInTheHint()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.ResolveAsync(graph, "Fin", None));

        Assert.Equal(ExitCode.NotFound, exception.Failure.ExitCode);
        Assert.Contains("2 groups", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Finance (00000000-0000-0000-0000-000000000031)", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Contains("Finance EU (00000000-0000-0000-0000-000000000032)", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Nothing")]
    [InlineData("00000000-0000-0000-0000-000000000098")]
    public async Task NoMatchIsNotFound(string argument)
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.ResolveAsync(graph, argument, None));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Equal($"no group named '{argument}'", exception.Message);
    }

    [Fact]
    public async Task MembersFollowEveryPageAndAreSortedByName()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: false, None);

        GroupMembers members = Assert.IsType<GroupMembers>(result.Data);
        Assert.Equal(["Alice Example", "Bob Example", "Carol Example", "Finance EU"], members.Members.Select(member => member.DisplayName));
        Assert.Equal(["user", "user", "user", "group"], members.Members.Select(member => member.Type));
        // The next page keeps the advanced query's header.
        Assert.All(transport.Requests, request => Assert.Equal("eventual", request.Headers["ConsistencyLevel"]));
        Assert.Equal(3, graph.RequestLog.Ids.Count);
    }

    [Fact]
    public async Task TransitiveMembersMarkTheNestedOnes()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: true, None);

        GroupMembers members = result.Data!;
        Assert.True(members.Transitive);
        Assert.Equal(["Alice Example", "Bob Example", "Carol Example", "Dave Example", "FIN-KIOSK-01", "Finance EU"], members.Members.Select(member => member.DisplayName));
        Assert.Equal([false, false, false, true, true, false], members.Members.Select(member => member.Nested));
        Assert.Equal("device", members.Members.Single(member => member.DisplayName == "FIN-KIOSK-01").Type);
        // The resolution, the direct list over its two pages, and the transitive list beside it.
        Assert.Equal(4, transport.Requests.Count);
        Assert.Single(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/transitiveMembers", StringComparison.Ordinal));
        Assert.All(transport.Requests, request => Assert.Equal("eventual", request.Headers["ConsistencyLevel"]));
    }

    [Fact]
    public async Task TransitiveMembersWithViaNameTheGroupTheyCameThrough()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: false, via: true, None);

        GroupMembers members = result.Data!;
        Assert.True(members.Transitive);
        Assert.True(members.Via);
        Assert.Equal(["Finance EU"], members.Members.Single(member => member.DisplayName == "Dave Example").Via!.Select(group => group.DisplayName));
        Assert.Equal(["00000000-0000-0000-0000-000000000032"], members.Members.Single(member => member.DisplayName == "FIN-KIOSK-01").Via!.Select(group => group.Id));
        Assert.All(members.Members.Where(member => member.Nested == false), member => Assert.Null(member.Via));
        // The resolution, the direct list over two pages, the transitive list, and one $batch with a members request
        // per group of the transitive list (Finance EU).
        Assert.Equal(5, transport.Requests.Count);
        Assert.EndsWith("/$batch", transport.Requests[^1].Uri.AbsolutePath, StringComparison.Ordinal);
        GraphHttpRequest item = Assert.Single(transport.BatchItems);
        Assert.EndsWith("/groups/00000000-0000-0000-0000-000000000032/members", item.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("eventual", item.Headers["ConsistencyLevel"]);
    }

    [Fact]
    public void ViaFollowsEveryPathDownFromTheDirectGroupsAndEndsACycle()
    {
        GroupMember Group(string id) => new("group", id.ToUpperInvariant(), null, null, id);
        GroupMember User(string id) => new("user", id, null, null, id);
        // Top holds a and b; a holds c; b holds c and u2; c holds u1 and, in a cycle, a.
        IReadOnlyList<GroupMember> direct = [Group("a"), Group("b"), User("u0")];
        IReadOnlyList<GroupMember> all = [Group("a"), Group("b"), User("u0"), Group("c"), User("u1"), User("u2")];
        var membersOf = new Dictionary<string, IReadOnlyList<GroupMember>>
        {
            ["a"] = [Group("c")],
            ["b"] = [Group("c"), User("u2")],
            ["c"] = [User("u1"), Group("a")],
        };

        IReadOnlyList<GroupMember> marked = GroupLookups.Mark(direct, all, membersOf);

        string Via(string id) => string.Join(",", marked.Single(member => member.Id == id).Via?.Select(group => group.DisplayName) ?? ["(none)"]);
        Assert.Equal("A,B", Via("c"));
        Assert.Equal("A,B", Via("u1"));
        Assert.Equal("B", Via("u2"));
        Assert.Equal("(none)", Via("a"));
        Assert.Equal("(none)", Via("u0"));
    }

    [Fact]
    public async Task ThePlainTransitiveListSendsNoBatch()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: true, None);

        Assert.Null(result.Data!.Via);
        Assert.All(result.Data.Members, member => Assert.Null(member.Via));
        Assert.Empty(transport.BatchItems);
    }

    [Fact]
    public async Task DirectMembersCarryNoMembershipMark()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", transitive: false, None);

        Assert.False(result.Data!.Transitive);
        Assert.All(result.Data.Members, member => Assert.Null(member.Nested));
    }

    [Fact]
    public async Task OwnersOfAGroupWithoutOneAreEmpty()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<GroupOwners> result = await GroupLookups.OwnersAsync(graph, "Phoenix", None);

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Owners);
    }

    [Fact]
    public async Task GroupsWithoutAnOwnerLeaveOutTheOnesGraphKeepsNoOwnersFor()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(graph, allTypes: false, None);

        Assert.Equal(["Helpdesk Admins", "Project Phoenix"], result.Data!.Groups.Select(group => group.DisplayName));
        // The count of every ownerless group (5) less the two listed.
        Assert.Equal(3, result.Data.LeftOut);
        // Three requests side by side and no next page: the two kinds Graph keeps owners for, and the count.
        Assert.Equal(3, transport.Requests.Count);
        string[] queries = transport.Requests.Select(request => Uri.UnescapeDataString(request.Uri.PathAndQuery)).Order(StringComparer.Ordinal).ToArray();
        Assert.StartsWith("/v1.0/groups/$count?$filter=owners/$count eq 0", queries[0], StringComparison.Ordinal);
        Assert.Contains($"$filter={GroupLookups.OwnerlessMicrosoft365Filter}&", queries[1], StringComparison.Ordinal);
        Assert.Contains($"$filter={GroupLookups.OwnerlessSecurityFilter}&", queries[2], StringComparison.Ordinal);
        Assert.All(transport.Requests, request => Assert.Equal("eventual", request.Headers["ConsistencyLevel"]));
        Assert.Equal(3, graph.RequestLog.Ids.Count);
        // The raw $count answers text/plain only (a 415 when only JSON is accepted, found live on 2026-10-29).
        GraphHttpRequest count = Assert.Single(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/$count", StringComparison.Ordinal));
        Assert.Equal("text/plain", count.Headers["Accept"]);
        Assert.All(transport.Requests.Where(request => request != count), request => Assert.False(request.Headers.ContainsKey("Accept")));
    }

    [Fact]
    public async Task AllTypesListsEveryGroupWithoutAnOwner()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(graph, allTypes: true, None);

        Assert.Equal(
            [GroupKind.Distribution, GroupKind.Security, GroupKind.Security, GroupKind.Microsoft365, GroupKind.MailEnabledSecurity],
            result.Data!.Groups.Select(group => group.Kind));
        Assert.True(result.Data.Groups.Single(group => group.DisplayName == "HR Synced").Synced);
        Assert.Equal(0, result.Data.LeftOut);
        // The one wide query over its two pages.
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.Contains("owners/$count", Uri.UnescapeDataString(request.Uri.Query), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACountGraphRefusesFallsBackToTheWideQuery()
    {
        var transport = new ScriptedTransport(request =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/$count", StringComparison.Ordinal))
            {
                return new GraphResponse(415, [], "{\"error\":{\"code\":\"Request_BadRequest\",\"message\":\"A supported MIME type could not be found.\"}}");
            }

            return new GraphResponse(200, [], """
                {"value":[{"id":"00000000-0000-0000-0000-000000000034","displayName":"Helpdesk Admins","groupTypes":[],"securityEnabled":true}]}
                """);
        });

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(new GraphClient(transport), allTypes: false, None);

        Assert.True(result.Ok);
        Assert.Equal(["Helpdesk Admins"], result.Data!.Groups.Select(group => group.DisplayName));
        Assert.StartsWith("?$filter=owners/$count eq 0&", Uri.UnescapeDataString(transport.Requests[^1].Uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANarrowedFilterGraphRefusesFallsBackToTheWideQuery()
    {
        var transport = new ScriptedTransport(request =>
        {
            string query = Uri.UnescapeDataString(request.Uri.Query);
            if (query.Contains("Unified", StringComparison.Ordinal))
            {
                return new GraphResponse(400, [], "{\"error\":{\"code\":\"Request_UnsupportedQuery\",\"message\":\"Unsupported query.\"}}");
            }

            if (request.Uri.AbsolutePath.EndsWith("/$count", StringComparison.Ordinal))
            {
                return new GraphResponse(200, [], "2");
            }

            return new GraphResponse(200, [], """
                {"value":[
                  {"id":"00000000-0000-0000-0000-000000000034","displayName":"Helpdesk Admins","groupTypes":[],"securityEnabled":true},
                  {"id":"00000000-0000-0000-0000-000000000035","displayName":"All Staff","mail":"allstaff@contoso.com","groupTypes":[],"securityEnabled":false}
                ]}
                """);
        });
        var graph = new GraphClient(transport);

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(graph, allTypes: false, None);

        Assert.Equal(["Helpdesk Admins"], result.Data!.Groups.Select(group => group.DisplayName));
        Assert.Equal(1, result.Data.LeftOut);
        Assert.StartsWith("?$filter=owners/$count eq 0&", Uri.UnescapeDataString(transport.Requests[^1].Uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TeamsAreListedByName()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<IReadOnlyList<TeamInfo>> result = await WorkspaceLookups.TeamsAsync(graph, None);

        Assert.Equal(["Finance", "Project Phoenix"], result.Data!.Select(team => team.DisplayName));
        Assert.Equal("private", result.Data![0].Visibility);
    }

    [Fact]
    public async Task SitesAreEveryHitOfTheSearch()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<IReadOnlyList<SiteInfo>> all = await WorkspaceLookups.SitesAsync(graph, null, None);
        CommandResult<IReadOnlyList<SiteInfo>> some = await WorkspaceLookups.SitesAsync(graph, "phoenix", None);

        Assert.Equal(["Communication site", "Finance", "Project Phoenix"], all.Data!.Select(site => site.DisplayName));
        Assert.Equal("https://contoso.sharepoint.com/sites/phoenix", Assert.Single(some.Data!).WebUrl);
    }
}
