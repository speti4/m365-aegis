using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Core.Tests.Exchange;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// The snapshot store's edges (task 087): the objects that merge and the ones that do not, the closure's cycles, the
/// file names, the folder of a profile, the retention's rule, a file that is not a snapshot, and a take over a scripted
/// tenant whose sites, drives and owners fail in every way the reads tell apart.
/// </summary>
public sealed class SnapshotEdgeTests
{
    private const string U1 = "00000000-0000-0000-0000-0000000000d1";
    private const string U2 = "00000000-0000-0000-0000-0000000000d2";
    private const string G1 = "00000000-0000-0000-0000-0000000000e1";
    private const string G2 = "00000000-0000-0000-0000-0000000000e2";
    private const string G3 = "00000000-0000-0000-0000-0000000000e3";

    private static readonly string[] Unified = ["Unified"];

    private static readonly string[] Read = ["read"];

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ObjectsMergeByIdOrUserPrincipalNameAndOtherwiseByKindAndLabel()
    {
        var builder = new SnapshotBuilder();

        SnapshotObject alice = builder.Object("user", U1, "alice@contoso.com", "Alice", null);
        SnapshotObject again = builder.Object("user", U2, "ALICE@contoso.com", null, "alice@contoso.com");
        SnapshotObject byAlias = builder.Object("user", U2.ToUpperInvariant(), null, null, null);
        SnapshotObject other = builder.Object("user", "00000000-0000-0000-0000-0000000000d3", "  ", "Other", null);
        // An object found by its id whose user principal name another object carries keeps none.
        SnapshotObject conflict = builder.Object("user", "00000000-0000-0000-0000-0000000000d3", "alice@contoso.com", null, null);
        SnapshotObject role = builder.Object("recipient", "Organization Management", null, "Organization Management", null);
        SnapshotObject sameRole = builder.Object("recipient", null, null, "Organization Management", null);
        SnapshotObject external = builder.Object("external", null, null, "Partner", "partner@fabrikam.com");
        SnapshotObject renamed = builder.Object("external", null, null, "Partner Example", "partner@fabrikam.com");

        Assert.Same(alice, again);
        Assert.Same(alice, byAlias);
        Assert.Equal((U1, "alice@contoso.com", "Alice", "alice@contoso.com"), (alice.ObjectId, alice.UserPrincipalName, alice.Name, alice.Mail));
        Assert.Same(other, conflict);
        Assert.Null(conflict.UserPrincipalName);
        Assert.Same(role, sameRole);
        Assert.Null(role.ObjectId);
        Assert.Same(external, renamed);
        Assert.Equal(4, builder.Objects.Count);
        Assert.Null(builder.Find(null, null));
        Assert.Same(alice, builder.Find(null, "alice@contoso.com"));
    }

    [Fact]
    public void TheNameIndexNamesAnObjectOnlyWhenOneCarriesTheName()
    {
        var builder = new SnapshotBuilder();
        builder.Object("user", U1, "alice@contoso.com", "Finance", "alice@contoso.com");
        builder.Group(new GroupInfo(G1, "Finance", GroupKind.Security, null, Synced: false));

        Dictionary<string, SnapshotObject?> index = builder.NameIndex();

        Assert.Null(index["Finance"]);
        Assert.Equal(U1, index["alice@contoso.com"]!.ObjectId);
    }

    [Fact]
    public void TheClosureFollowsANestedGroupOnceAndEndsACycle()
    {
        var builder = new SnapshotBuilder();
        int user = builder.Object("user", U1, "alice@contoso.com", "Alice", null).Key;
        int other = builder.Object("user", U2, "bob@contoso.com", "Bob", null).Key;
        int top = builder.Object("group", G1, null, "Top", null).Key;
        int middle = builder.Object("group", G2, null, "Middle", null).Key;
        int empty = builder.Object("group", G3, null, "Empty", null).Key;
        int sub = builder.Object("group", "00000000-0000-0000-0000-0000000000e4", null, "Sub", null).Key;
        int device = builder.Object("device", "00000000-0000-0000-0000-0000000000f1", null, "Kiosk", null).Key;
        // Top holds alice and Middle; Middle holds alice, bob, Top again (a cycle), Empty, Sub and a device; Sub holds bob,
        // so bob is reached twice below Middle and counted once for Top.
        builder.Members.AddRange([(top, user), (top, middle), (middle, user), (middle, other), (middle, top), (middle, empty), (middle, device), (middle, sub), (sub, other)]);

        Assert.Equal(5, builder.Closure());

        Assert.Equal(
            [(user, top, (int?)null), (other, top, middle), (user, middle, null), (other, middle, null), (other, sub, null)],
            builder.UserGroups);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("work.")]
    [InlineData("work ")]
    [InlineData("work\tlab")]
    [InlineData("work/lab")]
    [InlineData("work:lab")]
    public void AProfileNameThatCannotBeAFolderIsAUsageError(string profile)
    {
        AegisException failure = Assert.Throws<AegisException>(() => SnapshotStore.Folder("home", profile));

        Assert.Equal(FailureKind.Usage, failure.Failure.Kind);
    }

    [Fact]
    public void TheFolderOfAProfileAndTheProfileOfACommand()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        Assert.Equal(Path.Combine("home", "snapshots", "Contoso Prod"), SnapshotStore.Folder("home", "Contoso Prod"));
        Assert.Equal("contoso", SnapshotStore.ProfileName(home, null));
        Assert.Equal("lab", SnapshotStore.ProfileName(home, "lab"));
        Assert.Equal(FailureKind.Auth, Assert.Throws<AegisException>(() => SnapshotStore.ProfileName(PlanSamples.TempHome(), " ")).Failure.Kind);
    }

    [Theory]
    [InlineData("2026-10-31T060000Z.sqlite", true)]
    [InlineData("2026-10-31T060000Z.sqlite.partial", false)]
    [InlineData("2026-13-31T060000Z.sqlite", false)]
    [InlineData("notes.sqlite", false)]
    [InlineData("2026-10-31T060000Z.db", false)]
    public void ASnapshotsNameIsItsTime(string name, bool snapshot)
    {
        Assert.Equal(snapshot ? SnapshotSamples.Taken : null, SnapshotStore.TakenOf(name));
        Assert.Equal("2026-10-31T060000Z.sqlite", SnapshotStore.NameFor(SnapshotSamples.Taken.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public void TheRetentionKeepsTheNewestTheLastDaysAndADayAndAWeekEach()
    {
        // A Wednesday: its ISO week started on Monday the 2nd.
        DateTimeOffset now = Now;
        DateTimeOffset[] taken =
        [
            now.AddHours(-1), now.AddHours(-47), now.AddHours(-51), new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
        ];

        // Four days and this week: the last two days, and the newest of the 1st; the 2nd's older one and the 1st's
        // older one are not their day's newest.
        Assert.Equal([taken[3], taken[1], taken[0]], SnapshotStore.Keep(taken, now, new SnapshotRetention(4, 1)).Order());
        // Today and three weeks: the last two days, the newest of the week of the 26th (the 1st) and of the 19th.
        Assert.Equal([taken[6], taken[3], taken[1], taken[0]], SnapshotStore.Keep(taken, now, new SnapshotRetention(1, 3)).Order());
        Assert.Empty(SnapshotStore.Keep([], now, new SnapshotRetention()));
        Assert.Equal([taken[5]], SnapshotStore.Keep([taken[5]], now, new SnapshotRetention()));
    }

    [Fact]
    public void ARetentionBelowOneIsAUsageErrorAndNoneIsTheDefault()
    {
        TenantProfile profile = SnapshotSamples.Profile;

        Assert.Equal(new SnapshotRetention(), SnapshotTake.RetentionOf(profile));
        Assert.Equal(new SnapshotRetention(3, 1), SnapshotTake.RetentionOf(profile with { KeepDaily = 3, KeepWeekly = 1 }));
        Assert.Equal(FailureKind.Usage, Assert.Throws<AegisException>(() => SnapshotTake.RetentionOf(profile with { KeepDaily = 0 })).Failure.Kind);
        Assert.Equal(FailureKind.Usage, Assert.Throws<AegisException>(() => SnapshotTake.RetentionOf(profile with { KeepWeekly = -1 })).Failure.Kind);
    }

    [Fact]
    public void AProfilesRetentionIsWrittenByHandAndALoginKeepsIt()
    {
        string home = PlanSamples.TempHome();
        var store = new ProfileStore(home);
        store.Save(SnapshotSamples.Profile);
        Assert.Null(store.Resolve("contoso").KeepDaily);
        File.WriteAllText(store.Path, File.ReadAllText(store.Path).Replace("\"account\"", "\"keepDaily\": 3, \"keepWeekly\": 2, \"account\"", StringComparison.Ordinal));

        store.Save(SnapshotSamples.Profile with { Account = "bob@contoso.com" });
        TenantProfile kept = store.Resolve("contoso");
        store.Save(SnapshotSamples.Profile with { KeepDaily = 7 });
        TenantProfile daily = store.Resolve(null);
        store.Save(SnapshotSamples.Profile with { KeepWeekly = 5 });

        Assert.Equal(("bob@contoso.com", 3, 2), (kept.Account, kept.KeepDaily, kept.KeepWeekly));
        Assert.Equal((7, 2), (daily.KeepDaily, daily.KeepWeekly));
        Assert.Equal((7, 5), (store.Resolve(null).KeepDaily, store.Resolve(null).KeepWeekly));
    }

    [Theory]
    [InlineData(null, "recipient")]
    [InlineData("MailUniversalSecurityGroup", "group")]
    [InlineData("GroupMailbox", "group")]
    [InlineData("SharedMailbox", "user")]
    [InlineData("MailContact", "recipient")]
    public void AGrantsRecipientIsAGroupAUserOrARecipient(string? type, string kind) => Assert.Equal(kind, SnapshotTake.KindOf(type));

    [Theory]
    [InlineData(null, null)]
    [InlineData("/drives/x/root:", "/")]
    [InlineData("/drives/x/root:/Reports/2026", "/Reports/2026")]
    [InlineData("/sites/x/Shared Documents", "/sites/x/Shared Documents")]
    public void AnItemsFolderIsItsPathInTheLibrary(string? path, string? folder) => Assert.Equal(folder, SnapshotTake.FolderOf(path));

    [Fact]
    public async Task FilesThatAreNotSnapshotsAreNotReadAndAPruneThatIsRefusedWaits()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        Assert.Empty(SnapshotStore.List(home, "contoso").Snapshots);
        string folder = SnapshotStore.Folder(home, "contoso");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "2026-01-01T000000Z.sqlite"), "not a database", TestContext.Current.CancellationToken);
        string other = Path.Combine(folder, "2026-01-02T000000Z.sqlite");
        using (var connection = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "pragma user_version = 99; create table t (x)";
            command.ExecuteNonQuery();
        }

        await SnapshotSamples.SeedAsync(home, SnapshotSamples.Taken);
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.partial.sqlite.partial"), "", TestContext.Current.CancellationToken);

        SnapshotList list = SnapshotStore.List(home, "contoso");

        Assert.Equal(2, list.Unreadable);
        Assert.Single(list.Snapshots);
        Assert.Equal(list.Snapshots[0].Bytes, list.Bytes);
        Assert.Empty(SnapshotStore.RemovePartials(folder));
        Assert.Equal("2026-10-31T060000Z.sqlite", SnapshotStore.Show(home, "contoso", "2026-10-31T060000Z").Snapshot.Name);
        Assert.Equal("2026-10-31T060000Z.sqlite", SnapshotStore.Show(home, "contoso", "2026-10-31T060000Z.sqlite").Snapshot.Name);
        foreach (string name in new[] { "2026-01-01T000000Z", "2026-01-03T000000Z", "notes" })
        {
            Assert.Equal(FailureKind.NotFound, Assert.Throws<AegisException>(() => SnapshotStore.Show(home, "contoso", name)).Failure.Kind);
        }

        (IReadOnlyList<string> pruned, IReadOnlyList<string> refused) = SnapshotStore.Prune(
            folder, new SnapshotRetention(1, 1), Now.AddYears(1), file => throw (file.EndsWith("01T000000Z.sqlite", StringComparison.Ordinal)
                ? new IOException($"{Path.GetFileName(file)} is in use")
                : new UnauthorizedAccessException("access denied")));

        Assert.Empty(pruned);
        Assert.Equal(
            ["2026-01-01T000000Z.sqlite could not be pruned (2026-01-01T000000Z.sqlite is in use); the next take tries again",
             "2026-01-02T000000Z.sqlite could not be pruned (access denied); the next take tries again"],
            refused);
    }

    [Fact]
    public async Task AScriptedTenantFailsSiteBySiteDriveByDriveAndOwnerByOwner()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        var graph = new ScriptedGraph();
        graph.Answer("/users", 200, Page(new { id = U1, displayName = "Alice", userPrincipalName = "alice@contoso.com" }));
        graph.Answer("/groups", 200, Page(
            new { id = G1, displayName = "Team", groupTypes = Unified, securityEnabled = false },
            new { id = G2, displayName = "List", mail = "list@contoso.com", groupTypes = Array.Empty<string>(), securityEnabled = false }));
        graph.Answer($"/groups/{G1}/members", 200, Page(new Dictionary<string, object> { ["@odata.type"] = "#microsoft.graph.group", ["id"] = G3, ["displayName"] = "Nested" }));
        graph.Answer($"/groups/{G2}/members", 200, Page());
        graph.Answer($"/groups/{G1}/owners", 200, Page());
        graph.Answer("/teams", 200, Page());
        graph.Answer("/sites", 200, Page(Site("s1"), Site("s2"), Site("s3"), Site("s4"), Site("s5")));
        graph.Answer("/sites/s1/drive", 404, Error("itemNotFound"));
        graph.Answer("/sites/s2/drive", 200, new { id = "d2", owner = new { user = new { displayName = "Someone" } } });
        graph.Answer("/sites/s3/drive", 200, new { id = "d3", owner = new { group = new { id = G1, displayName = "Team" } } });
        graph.Answer("/sites/s4/drive", 500, Error("generalException"));
        graph.Answer("/sites/s5/drive", 200, new { id = "d5" });
        graph.Answer("/drives/d2/root/delta", 200, new { value = new object[] { new { id = "i2", name = "Shared.docx", shared = new { } } } });
        graph.Answer("/drives/d3/root/delta", 403, Error("accessDenied"));
        graph.Answer("/drives/d5/root/delta", 200, new { value = Array.Empty<object>() });
        graph.Answer("/drives/d2/items/i2/permissions", 404, Error("itemNotFound"));
        // One mailbox, whose FullAccess holder is a security identifier no recipient answers to and whose forwarding
        // target no recipient answers to either.
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetDistributionGroup => throw new AegisException(FailureKind.CmdletFailed, "the group is gone"),
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items("""
                {"DisplayName":"Ghost","PrimarySmtpAddress":"ghost@contoso.com","RecipientTypeDetails":"SharedMailbox","ForwardingAddress":"Nobody","GrantSendOnBehalfTo":[]}
                """),
            ExoCmdlets.GetMailboxPermission => MailboxEdgeTests.Items("""
                {"Identity":"ghost@contoso.com","User":"S-1-5-21-1","AccessRights":["FullAccess"],"IsInherited":false,"Deny":false}
                """),
            _ => new ExoResult([], []),
        });
        var backends = new ExchangeBackends(sidecar, null, () => graph.Client);

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(home, Now, backends: backends)).Data!;

        Dictionary<string, SnapshotDomain> domains = take.Domains.ToDictionary(domain => domain.Name);
        Assert.Equal(SnapshotDomainStatus.Partial, domains["owners"].Status);
        Assert.Equal("1 could not be read (cmdlet-failed 1), for example List: the group is gone", domains["owners"].Note);
        Assert.Equal(SnapshotDomainStatus.Partial, domains["sites"].Status);
        Assert.StartsWith("1 could not be read (service-error 1), for example Site s4: Microsoft Graph answered 500", domains["sites"].Note, StringComparison.Ordinal);
        Assert.EndsWith("; no document library: Site s1", domains["sites"].Note, StringComparison.Ordinal);
        Assert.Equal(SnapshotDomainStatus.Partial, domains["sharing"].Status);
        Assert.Contains("Site s3: Microsoft Graph answered 403", domains["sharing"].Note, StringComparison.Ordinal);
        Assert.Contains("Site s2: Shared.docx: Microsoft Graph answered 404", domains["sharing"].Note, StringComparison.Ordinal);
        Assert.Equal((SnapshotDomainStatus.Ok, 0), (domains["userGroups"].Status, domains["userGroups"].Rows));
        Assert.Equal((SnapshotDomainStatus.Ok, 1), (domains["grants"].Status, domains["grants"].Rows));
        Assert.Equal((SnapshotDomainStatus.Ok, 1), (domains["forwarding"].Status, domains["forwarding"].Rows));
        Assert.Equal(SnapshotDomainStatus.Failed, domains["folderPermissions"].Status);
        Assert.Equal(SnapshotTake.HierarchicalSharing, graph.Requests.First(request => request.Uri.AbsolutePath.EndsWith("/delta", StringComparison.Ordinal)).Headers["Prefer"]);
        Assert.Equal(1, take.Domains.Count(domain => domain.Status == SnapshotDomainStatus.Failed));
    }

    [Fact]
    public async Task AFailedGroupsReadFailsTheDomainsThatNeedItAndEveryDomainFailedIsNoSnapshot()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        var graph = new ScriptedGraph();
        var sidecar = new CannedRunner(_ => throw new AegisException(FailureKind.ServiceError, "the sidecar is down"));
        var backends = new ExchangeBackends(sidecar, null, () => graph.Client);

        AegisException failure = await Assert.ThrowsAsync<AegisException>(() => SnapshotSamples.TakeAsync(home, Now, backends: backends));

        Assert.Equal(FailureKind.NotFound, failure.Failure.Kind);
        Assert.StartsWith("Microsoft Graph answered 404 to GET /users", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(SnapshotStore.Folder(home, "contoso")));
    }

    [Fact]
    public async Task AFailedSignInStopsTheTake()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        var graph = new ScriptedGraph();
        graph.Answer("/users", 200, Page());
        graph.Answer("/groups", 200, Page());
        graph.Answer("/teams", 200, Page());
        graph.Answer("/sites", 200, Page());
        var sidecar = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox
            ? new ExoResult([MailboxEdgeTests.Object("""{"DisplayName":"Alice","PrimarySmtpAddress":"alice@contoso.com","UserPrincipalName":"alice@contoso.com"}""")], [])
            : new ExoResult([], []));
        // The Admin API refuses the mailbox list (the sidecar answers it) and then the sign-in, under the calendars' read.
        var admin = new CannedRunner(command => throw (command.Cmdlet == AdminApiCmdlets.GetMailbox
            ? new AegisException(FailureKind.Forbidden, "refused")
            : new AegisException(FailureKind.Auth, "no sign-in")));
        var backends = new ExchangeBackends(sidecar, admin, () => graph.Client);

        AegisException failure = await Assert.ThrowsAsync<AegisException>(() => SnapshotSamples.TakeAsync(home, Now, backends: backends));

        Assert.Equal(FailureKind.Auth, failure.Failure.Kind);
    }

    [Fact]
    public async Task ASignInRefusedWhileADriveOrAnOwnerIsReadStopsTheTake()
    {
        foreach (string refused in new[] { "/drives/d1/root/delta", "/drives/d1/items/i1/permissions" })
        {
            var graph = new ScriptedGraph();
            graph.Answer("/users", 200, Page());
            graph.Answer("/groups", 200, Page());
            graph.Answer("/teams", 200, Page());
            graph.Answer("/sites", 200, Page(Site("s1")));
            graph.Answer("/sites/s1/drive", 200, new { id = "d1" });
            graph.Answer(refused, 401, Error("InvalidAuthenticationToken"));
            graph.Answer("/drives/d1/root/delta", 200, new { value = new object[] { new { id = "i1", name = "A", shared = new { } } } });
            graph.Answer("/drives/d1/items/i1/permissions", 200, Page());
            var backends = new ExchangeBackends(new CannedRunner(_ => new ExoResult([], [])), null, () => graph.Client);

            AegisException failure = await Assert.ThrowsAsync<AegisException>(() => SnapshotSamples.TakeAsync(SnapshotSamples.WriteProfile(PlanSamples.TempHome()), Now, backends: backends));

            Assert.Equal(FailureKind.Auth, failure.Failure.Kind);
        }

        var owners = new ScriptedGraph();
        owners.Answer("/users", 200, Page());
        owners.Answer("/groups", 200, Page(new { id = G2, displayName = "List", mail = "list@contoso.com", groupTypes = Array.Empty<string>(), securityEnabled = false }));
        owners.Answer($"/groups/{G2}/members", 200, Page());
        var sidecar = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetDistributionGroup ? throw new AegisException(FailureKind.Auth, "no sign-in") : new ExoResult([], []));

        AegisException ownerFailure = await Assert.ThrowsAsync<AegisException>(() =>
            SnapshotSamples.TakeAsync(SnapshotSamples.WriteProfile(PlanSamples.TempHome()), Now, backends: new ExchangeBackends(sidecar, null, () => owners.Client)));

        Assert.Equal(FailureKind.Auth, ownerFailure.Failure.Kind);
    }

    [Fact]
    public async Task AThrottledPermissionReadIsSentAgainPastACommandsBudget()
    {
        var graph = new ScriptedGraph();
        graph.Answer("/users", 200, Page());
        graph.Answer("/groups", 200, Page());
        graph.Answer("/teams", 200, Page());
        graph.Answer("/sites", 200, Page(Site("s1"), Site("s2")));
        graph.Answer("/sites/s1/drive", 200, new { id = "d1" });
        graph.Answer("/sites/s2/drive", 200, new { id = "d2" });
        graph.Answer("/drives/d1/root/delta", 200, new { value = new object[] { new { id = "i1", name = "A", shared = new { } } } });
        graph.Answer("/drives/d2/root/delta", 200, new { value = new object[] { new { id = "i2", name = "B", shared = new { } } } });
        // Five 429s of fifty seconds each: past a command's four retries and its minute, within the take's budget.
        foreach (int _ in Enumerable.Range(0, 5))
        {
            graph.Answer("/drives/d1/items/i1/permissions", 429, Error("activityLimitReached"), retryAfter: "50");
        }

        graph.Answer("/drives/d1/items/i1/permissions", 200, Page(new { roles = Read, link = new { scope = "organization", type = "view" } }));
        graph.Answer("/drives/d2/items/i2/permissions", 200, Page());
        var backends = new ExchangeBackends(new CannedRunner(_ => new ExoResult([], [])), null, () => graph.Client);

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(SnapshotSamples.WriteProfile(PlanSamples.TempHome()), Now, backends: backends)).Data!;

        SnapshotDomain sharing = take.Domains.Single(domain => domain.Name == "sharing");
        Assert.Equal((SnapshotDomainStatus.Ok, 1, 0), (sharing.Status, sharing.Rows, sharing.ItemsFailed));
        Assert.Equal(6, graph.Asked("/drives/d1/items/i1/permissions"));
        // Two deltas, the first drive's batch six times and the second's once (task 097); the five waits of fifty seconds are the domain's.
        Assert.Equal((9L, 250_000L), (sharing.GraphRequests, sharing.ThrottledMs));
    }

    [Fact]
    public async Task TheOwnersOfTheGroupsExchangeOwnsAreReadInOneBulkCall()
    {
        ScriptedGraph Tenant()
        {
            var graph = new ScriptedGraph();
            graph.Answer("/users", 200, Page());
            graph.Answer("/groups", 200, Page(
                new { id = G1, displayName = "List", mail = "list@contoso.com", groupTypes = Array.Empty<string>(), securityEnabled = false },
                new { id = G2, displayName = "Gone", mail = "gone@contoso.com", groupTypes = Array.Empty<string>(), securityEnabled = false }));
            graph.Answer($"/groups/{G1}/members", 200, Page());
            graph.Answer($"/groups/{G2}/members", 200, Page());
            return graph;
        }

        // Exchange lists a group without an object id, List without an owner, and not Gone: no name to resolve.
        var unowned = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetDistributionGroup
            ? MailboxEdgeTests.Items("""{"ManagedBy":["someone"]}""", $$"""{"ExternalDirectoryObjectId":"{{G1}}","ManagedBy":[]}""")
            : new ExoResult([], []));
        SnapshotTakeResult first = (await SnapshotSamples.TakeAsync(SnapshotSamples.WriteProfile(PlanSamples.TempHome()), Now, backends: new ExchangeBackends(unowned, null, () => Tenant().Client))).Data!;

        SnapshotDomain owners = first.Domains.Single(domain => domain.Name == "owners");
        Assert.Equal((SnapshotDomainStatus.Partial, 0, "1 could not be read (not-found 1), for example Gone: not among the groups Get-DistributionGroup lists"), (owners.Status, owners.Rows, owners.Note));
        Assert.Equal("Unlimited", Assert.Single(unowned.Commands, command => command.Cmdlet == ExoCmdlets.GetDistributionGroup).Parameters["ResultSize"]);
        Assert.DoesNotContain(unowned.Commands, command => command.Cmdlet == ExoCmdlets.GetRecipient);

        // Both groups listed, their owners a role group no recipient answers to: listed by its name, one call each way.
        var roles = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetDistributionGroup => MailboxEdgeTests.Items(
                $$"""{"ExternalDirectoryObjectId":"{{G1}}","ManagedBy":["Organization Management"]}""",
                $$"""{"ExternalDirectoryObjectId":"{{G2}}","ManagedBy":["Organization Management"]}"""),
            ExoCmdlets.GetRecipient => throw new AegisException(FailureKind.CmdletFailed, "no such recipient"),
            _ => new ExoResult([], []),
        });
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        SnapshotTakeResult second = (await SnapshotSamples.TakeAsync(home, Now, backends: new ExchangeBackends(roles, null, () => Tenant().Client))).Data!;

        Assert.Equal((SnapshotDomainStatus.Ok, 2), (second.Domains.Single(domain => domain.Name == "owners").Status, second.Domains.Single(domain => domain.Name == "owners").Rows));
        Assert.Equal(
            [(ExoCmdlets.GetDistributionGroup, 1), (ExoCmdlets.GetRecipient, 1)],
            roles.Commands.Where(command => command.Cmdlet is ExoCmdlets.GetDistributionGroup or ExoCmdlets.GetRecipient).GroupBy(command => command.Cmdlet).Select(group => (group.Key, group.Count())));
        Assert.Equal(["Organization Management"], Assert.Single(roles.Commands, command => command.Cmdlet == ExoCmdlets.GetRecipient).Input);
    }

    [Fact]
    public async Task ACalendarHolderNoObjectAnswersToIsCountedInTheDomainsNote()
    {
        var graph = new ScriptedGraph();
        graph.Answer("/users", 200, Page(new { id = U1, displayName = "Alice", userPrincipalName = "alice@contoso.com" }));
        graph.Answer("/groups", 200, Page());
        graph.Answer("/teams", 200, Page());
        graph.Answer("/sites", 200, Page());
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items("""{"DisplayName":"Alice","PrimarySmtpAddress":"alice@contoso.com","UserPrincipalName":"alice@contoso.com"}"""),
            ExoCmdlets.GetMailboxFolderStatistics => MailboxEdgeTests.Items("""{"Name":"Calendar","FolderPath":"/Calendar","FolderType":"Calendar"}"""),
            _ => new ExoResult([], []),
        });
        // Default and Anonymous are everyone's; Ghost is no object of the snapshot.
        var admin = new CannedRunner(command => command.Cmdlet == AdminApiCmdlets.GetMailbox
            ? throw new AegisException(FailureKind.Forbidden, "refused")
            : MailboxEdgeTests.Items(
                """{"User":"Default","AccessRights":["AvailabilityOnly"]}""",
                """{"User":"Anonymous","AccessRights":["None"]}""",
                """{"User":{"DisplayName":"Ghost"},"AccessRights":["Reviewer"]}"""));
        var backends = new ExchangeBackends(sidecar, admin, () => graph.Client);

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(SnapshotSamples.WriteProfile(PlanSamples.TempHome()), Now, backends: backends)).Data!;

        SnapshotDomain calendars = take.Domains.Single(domain => domain.Name == "folderPermissions");
        Assert.Equal((SnapshotDomainStatus.Ok, 3, "1 holder(s) matched no object"), (calendars.Status, calendars.Rows, calendars.Note));
    }

    [Fact]
    public async Task ADomainOfManyFailedItemsKeepsItsNoteShort()
    {
        var graph = new ScriptedGraph();
        graph.Answer("/groups", 200, Page());
        graph.Answer("/teams", 200, Page());
        graph.Answer("/sites", 200, Page(Site("s1")));
        graph.Answer("/sites/s1/drive", 200, new { id = "d1" });
        graph.Answer("/users", 200, Page(new { id = U1, displayName = "X", userPrincipalName = "x@contoso.com" }));
        graph.Answer("/drives/d1/root/delta", 200, new
        {
            value = Enumerable.Range(1, 5).Select(index => new { id = $"i{index}", name = $"File {index}.docx", shared = new { } }).ToArray(),
        });
        foreach (int index in Enumerable.Range(1, 4))
        {
            graph.Answer($"/drives/d1/items/i{index}/permissions", 403, Error("accessDenied"));
        }

        var backends = new ExchangeBackends(new CannedRunner(_ => new ExoResult([], [])), null, () => graph.Client);
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(home, Now, backends: backends)).Data!;

        SnapshotDomain sharing = take.Domains.Single(domain => domain.Name == "sharing");
        Assert.Equal((SnapshotDomainStatus.Partial, 5, "forbidden 4, not-found 1"), (sharing.Status, sharing.ItemsFailed, sharing.ItemFailures));
        Assert.StartsWith("5 could not be read (forbidden 4, not-found 1), for example Site s1: File 1.docx: Microsoft Graph answered 403", sharing.Note, StringComparison.Ordinal);
        Assert.Contains("; Site s1: File 3.docx: ", sharing.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("File 4.docx", sharing.Note, StringComparison.Ordinal);
        Assert.Contains("sharing is partial in this snapshot: 5 item(s) could not be read (forbidden 4, not-found 1); `m365-aegis snapshot show` names them", SnapshotAccess.ForUser(home, "contoso", "x@contoso.com", Now).Warnings);
    }

    private static object Site(string id) => new { id, displayName = $"Site {id}", webUrl = $"https://contoso.sharepoint.com/sites/{id}" };

    private static object Page(params object[] value) => new { value };

    private static object Error(string code) => new { error = new { code, message = code } };

    /// <summary>
    /// Graph over answers given by path (the query left out), a <c>$batch</c> call answered item by item; a path
    /// without an answer is a 404. Every request is kept.
    /// </summary>
    private sealed class ScriptedGraph
    {
        private readonly Dictionary<string, Queue<(int Status, string Body, string? RetryAfter)>> _answers = new(StringComparer.Ordinal);
        private readonly ScriptedTransport _transport;

        internal ScriptedGraph()
        {
            _transport = new ScriptedTransport(Send);
            Client = new GraphClient(_transport, options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask, Jitter = () => 0 });
        }

        internal GraphClient Client { get; }

        internal List<GraphHttpRequest> Requests => _transport.Requests;

        /// <summary>The path's answer; a second one for the same path answers after the first, and the last one stays.</summary>
        internal void Answer(string path, int status, object body, string? retryAfter = null)
        {
            if (!_answers.TryGetValue(path, out Queue<(int Status, string Body, string? RetryAfter)>? queue))
            {
                queue = new Queue<(int Status, string Body, string? RetryAfter)>();
                _answers[path] = queue;
            }

            queue.Enqueue((status, JsonSerializer.Serialize(body), retryAfter));
        }

        /// <summary>How many times the path was asked for.</summary>
        internal int Asked(string path) => Requests.Sum(request => request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal)
            ? JsonNode.Parse(request.Body!)!["requests"]!.AsArray().Count(item => item!["url"]!.GetValue<string>().Split('?')[0] == path)
            : request.Uri.AbsolutePath == "/v1.0" + path ? 1 : 0);

        private (int Status, string Body, string? RetryAfter) For(string url)
        {
            string path = Uri.UnescapeDataString(url.Split('?')[0]);
            path = path.StartsWith("/v1.0", StringComparison.Ordinal) ? path[5..] : path;
            if (!_answers.TryGetValue(path, out Queue<(int Status, string Body, string? RetryAfter)>? queue))
            {
                return (404, JsonSerializer.Serialize(Error("fixtureMissing")), null);
            }

            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }

        private GraphResponse Send(GraphHttpRequest request)
        {
            if (!request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal))
            {
                (int status, string body, _) = For(request.Uri.AbsolutePath);
                return new GraphResponse(status, [], body);
            }

            var responses = new JsonArray();
            foreach (JsonNode? item in JsonNode.Parse(request.Body!)!["requests"]!.AsArray())
            {
                (int status, string body, string? retryAfter) = For(item!["url"]!.GetValue<string>());
                var response = new JsonObject { ["id"] = item["id"]!.GetValue<string>(), ["status"] = status, ["body"] = JsonNode.Parse(body) };
                if (retryAfter is not null)
                {
                    response["headers"] = new JsonObject { ["Retry-After"] = retryAfter };
                }

                responses.Add(response);
            }

            return new GraphResponse(200, [], new JsonObject { ["responses"] = responses }.ToJsonString());
        }
    }
}
