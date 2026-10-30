using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// The group writes (task 052) over the recorded answers: Finance (…031) has the members Alice, Bob, Carol and Finance EU
/// and the one owner Alice; Dave (…005), Bob and Eve (…007) resolve through <c>/users</c>, nobody does not; adding Dave
/// and Bob as owner answer 204, adding Eve answers 403.
/// </summary>
public sealed class GroupWritesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APlanListsOnlyTheMemberNotThereYetAndSendsNothing()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, GroupWrites.FromArguments("Finance", ["dave@contoso.com", "bob@contoso.com"]), Ct);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal(PlanAction.Add, change.Action);
        Assert.Equal("Finance", change.Target);
        Assert.Equal("Dave Example (dave@contoso.com) as a member", change.Detail);
        Assert.Equal(("not a member", "a member"), (change.Current, change.Desired));
        PlanUnchanged unchanged = Assert.Single(write.Plan.Unchanged);
        Assert.Equal("Bob Example (bob@contoso.com) as a member", unchanged.Detail);
        Assert.Equal("group-members-add", write.Plan.Operation);
        Assert.Equal("Add members to Finance (00000000-0000-0000-0000-000000000031)", write.Plan.Title);
        Assert.Empty(write.Plan.Warnings);
        Assert.All(transport.Requests, request => Assert.True(request.Method == HttpMethod.Get || request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheApplyPostsTheReferenceAndRecordsIt()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        string home = PlanSamples.TempHome();
        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, GroupWrites.FromArguments("Finance", ["dave@contoso.com"]), Ct);

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        GraphHttpRequest post = Assert.Single(transport.Requests, request => request.Method == HttpMethod.Post && !request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal));
        Assert.Equal("https://graph.microsoft.com/v1.0/groups/00000000-0000-0000-0000-000000000031/members/$ref", post.Uri.ToString());
        Assert.Equal("""{"@odata.id":"https://graph.microsoft.com/v1.0/directoryObjects/00000000-0000-0000-0000-000000000005"}""", post.Body);
        Assert.Contains("00000000-0000-0000-0000-0000000000ec", graph.RequestLog.Ids);
        Assert.Equal(ChangeStatus.Applied, PlanStore.Read(result.Data!.Record!)!.Plan.Changes[0].Result!.Status);
    }

    [Fact]
    public async Task AUserToAddThatDoesNotExistFailsThePlanBeforeAnythingIsSent()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, GroupWrites.FromArguments("Finance", ["dave@contoso.com", "nobody@contoso.com"]), Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Equal("no user or object 'nobody@contoso.com' in the directory", exception.Failure.Message);
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/$ref", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARemovalListsTheMembersThereAndLeavesTheOthers()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: false, GroupWrites.FromArguments("Finance", ["carol@contoso.com", "dave@contoso.com", "00000000-0000-0000-0000-000000000004"]), Ct);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal(("Carol Example (carol@contoso.com) as a member", "a member", "not a member"), (change.Detail, change.Current, change.Desired));
        Assert.Equal(["not a member", "named twice: the change is planned once"], write.Plan.Unchanged.Select(unchanged => unchanged.State));

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.True(result.Ok);
        GraphHttpRequest delete = Assert.Single(transport.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal("https://graph.microsoft.com/v1.0/groups/00000000-0000-0000-0000-000000000031/members/00000000-0000-0000-0000-000000000004/$ref", delete.Uri.ToString());
    }

    [Fact]
    public async Task RemovingTheLastOwnerWarns()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Owners, add: false, GroupWrites.FromArguments("Finance", ["alice@contoso.com"]), Ct);

        Assert.Equal("Alice Example (alice@contoso.com) as an owner", Assert.Single(write.Plan.Changes).Detail);
        string warning = Assert.Single(write.Plan.Warnings);
        Assert.StartsWith("Finance loses its last owner", warning, StringComparison.Ordinal);
        Assert.Equal("group-owners-remove", write.Plan.Operation);

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.True(result.Ok);
        Assert.Equal("https://graph.microsoft.com/v1.0/groups/00000000-0000-0000-0000-000000000031/owners/00000000-0000-0000-0000-000000000001/$ref", Assert.Single(transport.Requests, request => request.Method == HttpMethod.Delete).Uri.ToString());
    }

    [Fact]
    public async Task AnOwnerToAddIsResolvedEvenWhenHeIsAMember()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Owners, add: true, GroupWrites.FromArguments("Finance", ["bob@contoso.com", "alice@contoso.com"]), Ct);
        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(("Bob Example (bob@contoso.com) as an owner", "not an owner", "an owner"), (write.Plan.Changes[0].Detail, write.Plan.Changes[0].Current, write.Plan.Changes[0].Desired));
        Assert.Equal("an owner already", Assert.Single(write.Plan.Unchanged).State);
        Assert.True(result.Ok);
        Assert.Contains(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/owners/$ref", StringComparison.Ordinal) && request.Body!.Contains("000000000003", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACsvFileIsOnePlanByItsLinesAndAFailedLineMakesItPartial()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        IReadOnlyList<GroupChangeRow> rows = GroupWrites.FromCsv(PlanSamples.Csv("group-members-add.csv"));

        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, rows, Ct);
        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal([2, 4], write.Plan.Changes.Select(change => change.Row!.Value));
        Assert.Equal(3, Assert.Single(write.Plan.Unchanged).Row);
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Failed], result.Data!.Changes.Select(change => change.Result!.Status));
        Assert.Equal("forbidden", result.Data.Changes[1].Result!.Error!.Code);
    }

    [Fact]
    public void TheArgumentsNeedAGroupAndAUser()
    {
        Assert.Equal(FailureKind.Usage, Assert.Throws<AegisException>(() => GroupWrites.FromArguments(null, ["bob@contoso.com"])).Failure.Kind);
        Assert.Equal(FailureKind.Usage, Assert.Throws<AegisException>(() => GroupWrites.FromArguments("Finance", [])).Failure.Kind);
    }
}
