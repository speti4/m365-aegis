using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The mailbox lookups over the three layers (tasks 042 to 044): the sidecar over the fake module, the Admin API and
/// Graph over the recorded answers. One sidecar for the class, as the window would keep it; the extra sidecars of a
/// parallel read are started per test and ended with its backends.
/// </summary>
public sealed class MailboxReadsTests(FakeExchange exchange) : IClassFixture<FakeExchange>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ExchangeBackends Backends(bool adminApi = true, bool graph = true, List<IExoCmdletRunner>? started = null) => new(
        exchange.Session,
        adminApi ? AdminApiConnection.Fixtures(GraphFixtures.Root) : null,
        graph ? () => GraphFixtures.Client().Client : null,
        () =>
        {
            ExchangeSession session = SidecarFixtures.Session();
            started?.Add(session);
            return session;
        });

    [Fact]
    public async Task AccessReadsTheDelegatesByNameFromTheAdminApiAndTheRightsFromTheSidecar()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct);

        MailboxAccess access = result.Data!;
        Assert.Equal("finance@contoso.com", access.Mailbox.PrimarySmtpAddress);
        Assert.Equal(
            [(MailboxRight.FullAccess, "alice@contoso.com", "alice@contoso.com"), (MailboxRight.SendAs, "alice@contoso.com", "alice@contoso.com"), (MailboxRight.SendOnBehalf, "Bob Example", "bob@contoso.com")],
            access.Holders.Select(holder => (holder.Right, holder.Holder, holder.Address)));
        Assert.All(access.Holders, holder => Assert.Null(holder.Group));
        Assert.Equal("admin-api+pwsh", backends.Backend);

        // Each grant names the layer that read it (task 063): the delegate the Admin API's mailbox read, the rights the sidecar.
        Assert.Equal(Backend.AdminApi, access.MailboxLayer);
        Assert.Equal([Backend.Pwsh, Backend.Pwsh, Backend.AdminApi], access.Holders.Select(holder => holder.Layer));
    }

    [Fact]
    public async Task ARefusedDisplayNamesParameterIsAskedForAgainWithoutIt()
    {
        var transport = new FixtureGraphTransport(GraphFixtures.Root);
        var admin = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)));
        await using var backends = new ExchangeBackends(exchange.Session, admin);

        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(backends, "alice", new MailboxAccessOptions(), Ct);

        Assert.Equal("Alice Example", result.Data!.Mailbox.DisplayName);
        Assert.Empty(result.Data.Holders);
        Assert.Equal(2, transport.Requests.Count);
        Assert.DoesNotContain("IncludeGrantSendOnBehalfToWithDisplayNames", transport.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdminApiThatCannotAnswerLeavesTheReadToTheSidecar()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(backends, "bob", new MailboxAccessOptions(), Ct);

        Assert.True(result.Ok);
        Assert.Equal("Bob Example", result.Data!.Mailbox.DisplayName);
        Assert.Empty(result.Data.Holders);
        Assert.Equal(Backend.Pwsh, result.Data.MailboxLayer);
    }

    [Fact]
    public async Task AMailboxTheAdminApiDoesNotFindIsNotFoundAsGiven()
    {
        await using ExchangeBackends backends = Backends();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(backends, "nobody", new MailboxAccessOptions(), Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Equal("mailbox 'nobody' not found", exception.Message);
    }

    [Fact]
    public async Task WithoutAnAdminApiTheSidecarAnswersAlone()
    {
        await using ExchangeBackends backends = Backends(adminApi: false);

        CommandResult<MailboxAccess> layered = await MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct);
        CommandResult<MailboxAccess> sidecar = await MailboxLookups.AccessAsync(exchange.Session, "finance", Ct);

        // The same grants; the layered read names its one layer on each (task 063), the sidecar-only read none.
        Assert.Equal(sidecar.Data!.Holders.Select(holder => holder with { Layer = Backend.Pwsh }), layered.Data!.Holders);
        Assert.Equal(Backend.Pwsh, layered.Data.MailboxLayer);
        Assert.Null(sidecar.Data.MailboxLayer);
        Assert.All(sidecar.Data.Holders, holder => Assert.Null(holder.Layer));
        Assert.Equal("pwsh", backends.Backend);
    }

    [Fact]
    public async Task ExpandingAGroupHolderListsItsMembersOverEveryNestingLevel()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(backends, "reception", new MailboxAccessOptions(ExpandGroups: true), Ct);

        IReadOnlyList<MailboxHolder> holders = result.Data!.Holders;
        MailboxHolder group = Assert.Single(holders, holder => holder.Group == true);
        Assert.Equal("Finance Staff", group.Holder);
        Assert.Equal(["Carol Example", "Dave Example", "FIN-KIOSK-01", "Finance EU"], group.Members!.Select(member => member.DisplayName));
        GroupMember dave = group.Members!.Single(member => member.DisplayName == "Dave Example");
        Assert.True(dave.Nested);
        Assert.Equal("Finance EU", Assert.Single(dave.Via!).DisplayName);
        Assert.All(holders.Where(holder => holder != group), holder => Assert.False(holder.Group));
        Assert.All(holders.Where(holder => holder != group), holder => Assert.Null(holder.Members));
        Assert.Equal("admin-api+pwsh+graph", backends.Backend);
    }

    [Theory]
    [InlineData("carol@contoso.com", 2)]
    [InlineData("dave@contoso.com", 3)]
    public async Task TheReverseLookupFindsTheMailboxesAUserReachesThroughAGroup(string user, int groups)
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<UserAccess> result = await MailboxLookups.AccessForUserAsync(backends, user, new UserAccessOptions(ExpandGroups: true), Ct);

        UserGrant grant = Assert.Single(result.Data!.Grants);
        Assert.Equal((MailboxRight.FullAccess, "reception@contoso.com"), (grant.Right, grant.Mailbox.PrimarySmtpAddress));
        Assert.Equal("Finance Staff", grant.Via?.DisplayName);
        Assert.Equal(groups, result.Data.Groups);
        Assert.Equal("parallel", result.Data.Method);
    }

    [Fact]
    public async Task WithoutExpandingTheGroupGrantIsNotTheUsers()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<UserAccess> result = await MailboxLookups.AccessForUserAsync(backends, "carol@contoso.com", new UserAccessOptions(Method: PermissionReadMethod.Piped), Ct);

        Assert.Empty(result.Data!.Grants);
        Assert.Null(result.Data.Groups);
        Assert.Equal("pwsh", backends.Backend);
    }

    [Fact]
    public async Task TheParallelReadFindsWhatThePipedOneFinds()
    {
        var started = new List<IExoCmdletRunner>();
        await using ExchangeBackends backends = Backends(started: started);

        CommandResult<UserAccess> piped = await MailboxLookups.AccessForUserAsync(backends, "alice@contoso.com", new UserAccessOptions(Method: PermissionReadMethod.Piped), Ct);
        CommandResult<UserAccess> parallel = await MailboxLookups.AccessForUserAsync(backends, "alice@contoso.com", new UserAccessOptions(Sidecars: 3), Ct);

        Assert.Equal(piped.Data!.Grants, parallel.Data!.Grants);
        Assert.Equal(("piped", "parallel"), (piped.Data.Method, parallel.Data.Method));
        // Six mailboxes in three shares: the command's own sidecar and two more.
        Assert.Equal(2, started.Count);
    }

    [Fact]
    public async Task TheSidecarOnlyOverloadStaysPiped()
    {
        CommandResult<UserAccess> result = await MailboxLookups.AccessForUserAsync(exchange.Session, "alice@contoso.com", Ct);

        Assert.Equal(3, result.Data!.Grants.Count);
        Assert.Null(result.Data.Method);
    }

    [Fact]
    public async Task ParallelSizesAreThePipedOnes()
    {
        var started = new List<IExoCmdletRunner>();
        await using ExchangeBackends backends = Backends(started: started);

        CommandResult<MailboxSizes> piped = await MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(MailboxSizeMethod.Piped), Ct);
        CommandResult<MailboxSizes> parallel = await MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(MailboxSizeMethod.Parallel, 2), Ct);

        Assert.Equal(piped.Data!.Mailboxes, parallel.Data!.Mailboxes);
        Assert.Equal("parallel", parallel.Data.Method);
        Assert.Single(started);
    }

    [Fact]
    public async Task ReportSizesJoinTheUsageReportToTheAdminApisMailboxList()
    {
        await using ExchangeBackends backends = Backends();

        // Every mailbox's size comes from the report by default.
        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(backends, null, 2, new MailboxSizeOptions(), Ct);

        MailboxSizes sizes = result.Data!;
        Assert.Equal(["Finance", "Alice Example"], sizes.Mailboxes.Select(size => size.Mailbox.DisplayName));
        Assert.Equal(3221000000, sizes.Mailboxes[0].TotalItemBytes);
        Assert.Equal("SharedMailbox", sizes.Mailboxes[0].Mailbox.Type);
        Assert.Equal(6, sizes.MailboxesScanned);
        Assert.Equal(1, sizes.NotInReport);
        Assert.Equal(new DateOnly(2026, 10, 28), sizes.ReportDate);
        Assert.Equal("report", sizes.Method);
        Assert.Equal("graph+admin-api", backends.Backend);
    }

    [Fact]
    public async Task AReportWithoutItsPermissionNamesTheLiveWayInTheHint()
    {
        var refused = new M365Aegis.Core.Tests.Graph.ScriptedTransport(_ => new GraphResponse(403, [], """{"error":{"code":"S2SUnauthorized","message":"Invalid permission."}}"""));
        await using var backends = new ExchangeBackends(exchange.Session, AdminApiConnection.Fixtures(GraphFixtures.Root), () => new GraphClient(refused));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(), Ct));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("Reports.Read.All", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.EndsWith("`--method parallel` reads the live sizes", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneMailboxIsReadLiveByDefault()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(backends, "carol", null, new MailboxSizeOptions(), Ct);

        Assert.Equal("piped", result.Data!.Method);
        Assert.Equal(10485760, Assert.Single(result.Data.Mailboxes).TotalItemBytes);
        Assert.Equal("pwsh", backends.Backend);
    }

    [Fact]
    public async Task OneMailboxFromTheReport()
    {
        await using ExchangeBackends backends = Backends(adminApi: false);

        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(backends, "carol", null, new MailboxSizeOptions(MailboxSizeMethod.Report), Ct);

        MailboxSize size = Assert.Single(result.Data!.Mailboxes);
        Assert.Equal((298L, 10400000L), (size.ItemCount, size.TotalItemBytes!.Value));
    }

    [Fact]
    public void AConcealedReportIsNamedAsSuch()
    {
        IReadOnlyList<MailboxLookups.UsageRow> rows = MailboxLookups.UsageReport(
            "Report Refresh Date,User Principal Name,Display Name,Is Deleted,Item Count,Storage Used (Byte)\n2026-10-28,0123456789ABCDEF0123456789ABCDEF,\"Name, with a comma\",False,3,4096\n");

        MailboxLookups.UsageRow row = Assert.Single(rows);
        Assert.Equal(("0123456789ABCDEF0123456789ABCDEF", 3L, 4096L), (row.UserPrincipalName, row.ItemCount, row.StorageBytes!.Value));
    }

    [Fact]
    public async Task FolderPermissionsReadTheCalendarByDefault()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxFolderPermissions> result = await MailboxLookups.FolderPermissionsAsync(backends, "finance", null, Ct);

        MailboxFolderPermissions folder = result.Data!;
        Assert.Equal(("Calendar", "Calendar"), (folder.Folder, folder.FolderName));
        Assert.Equal(["Default", "Anonymous", "Alice Example"], folder.Permissions.Select(entry => entry.User));
        Assert.Equal(["None"], folder.Permissions[1].AccessRights);
        Assert.Equal(["Delegate", "CanViewPrivateItems"], folder.Permissions[2].SharingPermissionFlags);
        Assert.Empty(folder.Permissions[0].SharingPermissionFlags);
        Assert.Equal("admin-api+pwsh", backends.Backend);
    }

    [Fact]
    public async Task TheDefaultCalendarIsFoundUnderItsLocalizedName()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxFolderPermissions> result = await MailboxLookups.FolderPermissionsAsync(backends, "reception", null, Ct);

        MailboxFolderPermissions folder = result.Data!;
        Assert.Equal(("Kalender", "Kalender"), (folder.Folder, folder.FolderName));
        Assert.Equal(["Default", "Bob Example"], folder.Permissions.Select(entry => entry.User));
        Assert.Equal(["Reviewer"], folder.Permissions[1].AccessRights);
    }

    [Fact]
    public async Task AMissingMailboxIsNotFoundBeforeItsCalendarIsAskedFor()
    {
        await using ExchangeBackends backends = Backends();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.FolderPermissionsAsync(backends, "nobody", null, Ct));

        Assert.Equal("mailbox 'nobody' not found", exception.Message);
    }

    [Fact]
    public async Task AGivenFolderIsReadWithoutTheSidecar()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<MailboxFolderPermissions> result = await MailboxLookups.FolderPermissionsAsync(backends, "finance", "Calendar", Ct);

        Assert.Equal(3, result.Data!.Permissions.Count);
        Assert.Equal("admin-api", backends.Backend);
    }

    [Fact]
    public async Task AMissingFolderIsNotFoundWithTheLanguageInTheHint()
    {
        await using ExchangeBackends backends = Backends();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.FolderPermissionsAsync(backends, "finance", "/Missing/", Ct));

        Assert.Equal("folder 'Missing' of mailbox 'finance' not found", exception.Message);
        Assert.Contains("language", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADistributionGroupsMembersComeOverEveryPage()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<DistributionGroupMembers> result = await MailboxLookups.DistributionGroupMembersAsync(backends, "sales", Ct);

        Assert.Equal(["Alice Example", "Bob Example", "Dave Private"], result.Data!.Members.Select(member => member.DisplayName));
        Assert.True(result.Data.Members[0].Hidden);
        Assert.Equal("MailContact", result.Data.Members[2].Type);
    }

    [Fact]
    public async Task AMissingDistributionGroupIsNotFound()
    {
        await using ExchangeBackends backends = Backends();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.DistributionGroupMembersAsync(backends, "nobody", Ct));

        Assert.Equal("distribution group 'nobody' not found", exception.Message);
    }

    [Fact]
    public async Task TheOrganizationIsItsMailTipsAndItsDomainsTheDefaultFirst()
    {
        await using ExchangeBackends backends = Backends();

        CommandResult<ExchangeOrganization> result = await MailboxLookups.OrganizationAsync(backends, Ct);

        ExchangeOrganization organization = result.Data!;
        Assert.Equal(("contoso.onmicrosoft.com", true, false, 25L), (organization.Name, organization.MailTipsAllTipsEnabled!.Value, organization.MailTipsExternalRecipientsTipsEnabled!.Value, organization.MailTipsLargeAudienceThreshold!.Value));
        Assert.Equal(["contoso.com", "contoso.onmicrosoft.com"], organization.AcceptedDomains.Select(domain => domain.DomainName));
    }

    [Fact]
    public async Task AnAdminApiReadWithoutTheAdminApiSaysSo()
    {
        await using ExchangeBackends backends = Backends(adminApi: false);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.OrganizationAsync(backends, Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public async Task TheSidecarsAreTwoToEight(int sidecars)
    {
        await using ExchangeBackends backends = Backends();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(MailboxSizeMethod.Parallel, sidecars), Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }
}
