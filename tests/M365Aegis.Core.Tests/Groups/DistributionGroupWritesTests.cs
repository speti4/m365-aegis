using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// The members of a distribution or mail-enabled security group through Exchange (task 061): the group resolved and
/// told by Graph (Sales, a distribution group, …036; Finance Staff, a mail-enabled security group, …035), its current
/// members from the Admin API's recorded <c>Get-DistributionGroupMember</c> (Sales: Bob, the contact Dave Private and
/// the hidden Alice; Finance Staff: Carol), and each change through the fake module's
/// <c>Add-</c>/<c>Remove-DistributionGroupMember</c>, which records what it was sent in a file of the test's own.
/// </summary>
public sealed class DistributionGroupWritesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A sidecar over the fake module that records its writes in <paramref name="writes"/>.</summary>
    private static ExchangeSession Session(string writes)
    {
        SidecarStartInfo start = SidecarFixtures.StartInfo() with
        {
            Environment = new Dictionary<string, string>
            {
                ["M365AEGIS_EXO_MODULE_ROOT"] = SidecarFixtures.ModuleRoot,
                ["M365AEGIS_FAKE_EXO_WRITES"] = writes,
            },
        };
        return new ExchangeSession(new ExchangeSessionOptions
        {
            Start = cancellationToken => SidecarClient.StartAsync(start, cancellationToken),
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
            Clock = TimeProvider.System,
        });
    }

    private static string WritesFile()
    {
        string folder = PlanSamples.TempHome();
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "writes.jsonl");
    }

    private static string[] Writes(string file) => File.Exists(file) ? File.ReadAllLines(file) : [];

    private static Task<CommandResult<Plan>> ApplyAsync(WritePlan write) =>
        PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

    [Fact]
    public async Task ADistributionGroupIsPlannedFromTheAdminApiAndAppliedThroughTheSidecar()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, add: true, GroupWrites.FromArguments("Sales", ["dave@contoso.com", "bob@contoso.com", "alice@contoso.com"]), Ct);
        string planned = backends.Backend;
        CommandResult<Plan> result = await ApplyAsync(write);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal(("Sales", "Dave Example (dave@contoso.com) as a member", "not a member", "a member"), (change.Target, change.Detail, change.Current, change.Desired));
        // Bob is on the list; Alice too, hidden from the address lists, which only Exchange's list shows.
        Assert.Equal(["Bob Example (bob@contoso.com) as a member", "Alice Example (alice@contoso.com) as a member"], write.Plan.Unchanged.Select(unchanged => unchanged.Detail));
        Assert.Empty(write.Plan.Warnings);
        Assert.Equal("graph+admin-api", planned);
        Assert.True(result.Ok);
        Assert.Equal("graph+admin-api+pwsh", backends.Backend);
        string sent = Assert.Single(Writes(writes));
        Assert.Contains("\"cmdlet\":\"Add-DistributionGroupMember\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"Identity\":\"sales@contoso.com\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"Member\":\"dave@contoso.com\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"BypassSecurityGroupManagerCheck\"", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMailEnabledSecurityGroupsMemberIsRemovedThroughExchangeToo()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, add: false, GroupWrites.FromArguments("00000000-0000-0000-0000-000000000035", ["carol@contoso.com", "alice@contoso.com"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal("group-members-remove", write.Plan.Operation);
        Assert.Equal("Carol Example (carol@contoso.com) as a member", Assert.Single(write.Plan.Changes).Detail);
        Assert.Equal("not a member", Assert.Single(write.Plan.Unchanged).State);
        Assert.True(result.Ok);
        string sent = Assert.Single(Writes(writes));
        Assert.Contains("\"cmdlet\":\"Remove-DistributionGroupMember\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"Identity\":\"finance-staff@contoso.com\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"Member\":\"carol@contoso.com\"", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheAdminApiGraphsListIsTheCurrentState()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, null, () => GraphFixtures.Client().Client);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, add: false, GroupWrites.FromArguments("00000000-0000-0000-0000-000000000035", ["carol@contoso.com"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Single(write.Plan.Changes);
        Assert.True(result.Ok);
        Assert.Contains("\"Member\":\"carol@contoso.com\"", Assert.Single(Writes(writes)), StringComparison.Ordinal);
        Assert.Equal("graph+pwsh", backends.Backend);
    }

    [Fact]
    public async Task AChangeTheServiceRefusesFailsAloneAndTheRestIsApplied()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        // Eve is a user in Graph but no recipient in Exchange: the fake module refuses her as the service would.
        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, add: true, GroupWrites.FromArguments("Sales", ["eve@contoso.com", "dave@contoso.com"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal([ChangeStatus.Failed, ChangeStatus.Applied], result.Data!.Changes.Select(change => change.Result!.Status));
        Assert.Contains("\"Member\":\"dave@contoso.com\"", Assert.Single(Writes(writes)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMicrosoft365GroupStillGoesThroughGraph()
    {
        string writes = WritesFile();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => graph);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, add: true, GroupWrites.FromArguments("Finance", ["dave@contoso.com"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.True(result.Ok);
        Assert.Equal("graph", backends.Backend);
        Assert.Single(transport.BatchItems, request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath.EndsWith("/members/$ref", StringComparison.Ordinal));
        Assert.Empty(Writes(writes));
    }

    [Fact]
    public async Task OverGraphAloneTheRefusalOfTheServiceIsStillAWarning()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: false, GroupWrites.FromArguments("00000000-0000-0000-0000-000000000035", ["carol@contoso.com"]), Ct);

        Assert.Single(write.Plan.Changes);
        Assert.Equal(
            "Finance Staff is a mail-enabled security group: Exchange manages its members and Graph cannot change them, so the service will refuse the change",
            Assert.Single(write.Plan.Warnings));
    }

    [Fact]
    public async Task ADistributionGroupsOwnersArePlannedFromManagedByAndAppliedThroughTheSidecar()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        // Sales is managed by alice (ManagedBy names her mailbox), so only Dave is planned.
        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Owners, add: true, GroupWrites.FromArguments("Sales", ["dave@contoso.com", "alice@contoso.com"]), Ct);
        string planned = backends.Backend;
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal("group-owners-add", write.Plan.Operation);
        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal(("Sales", "Dave Example (dave@contoso.com) as an owner", "not an owner", "an owner"), (change.Target, change.Detail, change.Current, change.Desired));
        Assert.Equal("Alice Example (alice@contoso.com) as an owner", Assert.Single(write.Plan.Unchanged).Detail);
        Assert.Empty(write.Plan.Warnings);
        // The owners are no Admin API read: the group from Graph, ManagedBy from the sidecar.
        Assert.Equal("graph+pwsh", planned);
        Assert.True(result.Ok);
        string sent = Assert.Single(Writes(writes));
        Assert.Contains("\"cmdlet\":\"Set-DistributionGroup\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"Identity\":\"sales@contoso.com\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"ManagedBy\":{\"Add\":[\"dave@contoso.com\"]}", sent, StringComparison.Ordinal);
        Assert.Contains("\"BypassSecurityGroupManagerCheck\"", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMailEnabledSecurityGroupsOwnerIsRemovedThroughExchangeAndARoleGroupOwnerIsListedByName()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        WritePlan write = await GroupWrites.PlanAsync(
            backends, GroupRelation.Owners, add: false, GroupWrites.FromArguments("00000000-0000-0000-0000-000000000035", ["carol@contoso.com", "dave@contoso.com", "Organization Management"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal(
            ["Carol Example (carol@contoso.com) as an owner", "Organization Management (recipient, Organization Management) as an owner"],
            write.Plan.Changes.Select(change => change.Detail));
        Assert.Equal("not an owner", Assert.Single(write.Plan.Unchanged).State);
        // Two of the two owners go: the plan says so.
        Assert.Contains(write.Plan.Warnings, warning => warning.Contains("loses its last owner", StringComparison.Ordinal));
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Failed], result.Data!.Changes.Select(change => change.Result!.Status));
        string sent = Assert.Single(Writes(writes));
        Assert.Contains("\"Identity\":\"finance-staff@contoso.com\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"ManagedBy\":{\"Remove\":[\"carol@contoso.com\"]}", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMicrosoft365GroupsOwnersStillGoThroughGraph()
    {
        string writes = WritesFile();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => graph);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Owners, add: true, GroupWrites.FromArguments("Finance", ["bob@contoso.com"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.True(result.Ok);
        Assert.Equal("graph", backends.Backend);
        Assert.Single(transport.BatchItems, request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath.EndsWith("/owners/$ref", StringComparison.Ordinal));
        Assert.Empty(Writes(writes));
    }

    [Fact]
    public async Task ASyncedDistributionGroupsOwnersStayOnGraphWithTheWarning()
    {
        string writes = WritesFile();
        await using ExchangeSession session = Session(writes);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        WritePlan write = await GroupWrites.PlanAsync(backends, GroupRelation.Owners, add: true, GroupWrites.FromArguments("00000000-0000-0000-0000-00000000003a", ["bob@contoso.com"]), Ct);

        Assert.Single(write.Plan.Changes);
        Assert.Equal(
            "Sales Synced is synced from on-premises Active Directory: Graph cannot change its owners there, so the service will refuse the change",
            Assert.Single(write.Plan.Warnings));
        Assert.Equal("graph", backends.Backend);
        Assert.Empty(Writes(writes));
    }

    [Fact]
    public void TheOwnerCmdletsAreOnTheAllowListAndSetDistributionGroupChangesTheOwnersOnly()
    {
        Assert.True(ExoCmdlets.IsAllowed("Get-DistributionGroup"));
        Assert.True(ExoCmdlets.IsAllowed("Set-DistributionGroup"));
        Assert.Equal(["Identity", "ManagedBy", "BypassSecurityGroupManagerCheck", "Confirm"], ExoCmdlets.WriteParameters[ExoCmdlets.SetDistributionGroup]);
        AegisException off = Assert.Throws<AegisException>(() => ExoCmdlets.EnsureAllowed(
            new ExoCommand(ExoCmdlets.SetDistributionGroup) { Parameters = new Dictionary<string, object?> { ["Identity"] = "sales", ["HiddenFromAddressListsEnabled"] = true } }));
        Assert.Equal(FailureKind.CmdletNotAllowed, off.Failure.Kind);
        // The Admin API stays on its documented reads.
        Assert.Equal(FailureKind.CmdletNotAllowed, Assert.Throws<AegisException>(() => AdminApiCmdlets.Endpoint("Get-DistributionGroup")).Failure.Kind);
    }

    [Fact]
    public void TheMemberWritesAreOnTheAllowListWithTheirParameters()
    {
        Assert.True(ExoCmdlets.IsAllowed("Add-DistributionGroupMember"));
        Assert.True(ExoCmdlets.IsAllowed("Remove-DistributionGroupMember"));
        AegisException off = Assert.Throws<AegisException>(() => ExoCmdlets.EnsureAllowed(
            new ExoCommand(ExoCmdlets.AddDistributionGroupMember) { Parameters = new Dictionary<string, object?> { ["Identity"] = "sales", ["Member"] = "dave", ["DomainController"] = "dc" } }));
        Assert.Equal(FailureKind.CmdletNotAllowed, off.Failure.Kind);
    }
}
