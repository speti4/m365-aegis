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

    /// <summary>
    /// A sidecar over the fake module that records its writes in <paramref name="writes"/>; the cmdlets of
    /// <paramref name="noRole"/> are refused as for an account without the Exchange Online role (task 096).
    /// </summary>
    private static ExchangeSession Session(string writes, string? noRole = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["M365AEGIS_EXO_MODULE_ROOT"] = SidecarFixtures.ModuleRoot,
            ["M365AEGIS_FAKE_EXO_WRITES"] = writes,
        };
        if (noRole is not null)
        {
            environment["M365AEGIS_FAKE_EXO_NO_ROLE"] = noRole;
        }

        SidecarStartInfo start = SidecarFixtures.StartInfo() with { Environment = environment };
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
    public async Task TheOwnersReadOfADistributionGroupIsItsManagedByAndAMicrosoft365GroupsIsGraphs()
    {
        await using ExchangeSession session = Session(WritesFile());
        await using var exchange = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);
        await using var graph = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        // The read of task 076 is the owner writes' read (task 064): Finance Staff's ManagedBy, Carol resolved and the
        // role group by its name.
        CommandResult<GroupOwners> staff = await GroupLookups.OwnersAsync(exchange, "00000000-0000-0000-0000-000000000035", Ct);
        CommandResult<GroupOwners> finance = await GroupLookups.OwnersAsync(graph, "Finance", Ct);

        Assert.True(staff.Data!.FromExchange);
        Assert.Equal(["Carol Example", "Organization Management"], staff.Data.Owners.Select(owner => owner.DisplayName));
        Assert.Equal(("recipient", "Organization Management"), (staff.Data.Owners[1].Type, staff.Data.Owners[1].Id));
        Assert.Equal("graph+pwsh", exchange.Backend);
        Assert.Null(finance.Data!.FromExchange);
        Assert.NotEmpty(finance.Data.Owners);
        Assert.Equal("graph", graph.Backend);
        await Assert.ThrowsAsync<ArgumentNullException>(() => GroupLookups.OwnersAsync((ExchangeBackends)null!, "Finance", Ct));
    }

    [Fact]
    public async Task OwnersMissingAllTypesJudgesADistributionGroupByItsManagedBy()
    {
        await using ExchangeSession session = Session(WritesFile());
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => graph);

        // Task 089: Graph lists five groups without an owner; the fake module's ManagedBy names alice for the mail-enabled
        // security group Sales Managers and nobody for the distribution group All Staff.
        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(backends, allTypes: true, Ct);

        Assert.True(result.Ok);
        Assert.Equal(["All Staff", "Helpdesk Admins", "HR Synced", "Project Phoenix"], result.Data!.Groups.Select(group => group.DisplayName));
        Assert.DoesNotContain(result.Data.Groups, group => group.DisplayName == "Sales Managers");
        // The Microsoft 365 group and the security group stay Graph's answer, the synced group too.
        Assert.Equal(GroupKind.Microsoft365, result.Data.Groups.Single(group => group.DisplayName == "Project Phoenix").Kind);
        Assert.True(result.Data.Groups.Single(group => group.DisplayName == "HR Synced").Synced);
        Assert.Equal(1, result.Data.ManagedInExchange);
        Assert.Equal(0, result.Data.LeftOut);
        Assert.Equal("graph+pwsh", backends.Backend);
        // The one wide query over its two pages, and no request per group.
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task OwnersMissingAllTypesWithoutAnExchangeRoleIsGraphsListWithAWarning()
    {
        await using ExchangeSession session = Session(WritesFile(), noRole: "Get-DistributionGroup");
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        // Task 096: the fake module refuses the ManagedBy read as the service refuses an account without the role.
        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(backends, allTypes: true, Ct);

        Assert.True(result.Ok);
        Assert.Equal(["All Staff", "Helpdesk Admins", "HR Synced", "Project Phoenix", "Sales Managers"], result.Data!.Groups.Select(group => group.DisplayName));
        Assert.Null(result.Data.ManagedInExchange);
        Assert.Equal(0, result.Data.LeftOut);
        string warning = Assert.Single(result.Data.Warnings!);
        Assert.StartsWith(
            "the distribution and mail-enabled security groups are judged by Graph's empty owner list: Exchange Online refused the ManagedBy read (the Exchange Online cmdlet failed: fake: Access denied.",
            warning,
            StringComparison.Ordinal);
        Assert.EndsWith("(403).); the account needs an Exchange Online role that runs the cmdlet (View-Only Organization Management or Recipient Management for the mailbox lookups), and the app registration Exchange.Manage with admin consent", warning, StringComparison.Ordinal);
        Assert.Equal("graph", backends.Backend);
    }

    [Fact]
    public async Task OwnersMissingWithoutAllTypesAsksNothingOfExchange()
    {
        await using ExchangeSession session = Session(WritesFile());
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => GraphFixtures.Client().Client);

        CommandResult<OwnerlessGroups> result = await GroupLookups.OwnerlessAsync(backends, allTypes: false, Ct);

        Assert.Equal(["Helpdesk Admins", "Project Phoenix"], result.Data!.Groups.Select(group => group.DisplayName));
        Assert.Equal(3, result.Data.LeftOut);
        Assert.Null(result.Data.ManagedInExchange);
        Assert.Equal("graph", backends.Backend);
        await Assert.ThrowsAsync<ArgumentNullException>(() => GroupLookups.OwnerlessAsync((ExchangeBackends)null!, allTypes: true, Ct));
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
