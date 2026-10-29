using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
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

        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, "Finance", None);

        GroupMembers members = Assert.IsType<GroupMembers>(result.Data);
        Assert.Equal(["Alice Example", "Bob Example", "Carol Example", "Finance EU"], members.Members.Select(member => member.DisplayName));
        Assert.Equal(["user", "user", "user", "group"], members.Members.Select(member => member.Type));
        // The next page keeps the advanced query's header.
        Assert.All(transport.Requests, request => Assert.Equal("eventual", request.Headers["ConsistencyLevel"]));
        Assert.Equal(3, graph.RequestLog.Ids.Count);
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
        Assert.Equal(3, result.Data.LeftOut);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Contains("owners/$count eq 0", Uri.UnescapeDataString(transport.Requests[0].Uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllTypesListsEveryGroupWithoutAnOwner()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(graph, allTypes: true, None);

        Assert.Equal(
            [GroupKind.Distribution, GroupKind.Security, GroupKind.Security, GroupKind.Microsoft365, GroupKind.MailEnabledSecurity],
            result.Data!.Groups.Select(group => group.Kind));
        Assert.True(result.Data.Groups.Single(group => group.DisplayName == "HR Synced").Synced);
        Assert.Equal(0, result.Data.LeftOut);
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
