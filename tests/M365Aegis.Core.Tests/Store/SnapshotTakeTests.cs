using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Core.Tests.Exchange;
using M365Aegis.Tests;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// <c>snapshot take</c> over the recorded Graph and Admin API answers and the fake Exchange Online module (task 087):
/// every domain of D-011 into one SQLite file under the profile's folder, the <c>.partial</c> rename, the manifest, the
/// prune by the retention, and what a take does when a domain fails.
/// </summary>
public sealed class SnapshotTakeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATakeReadsEveryDomainIntoOneFile()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        CommandResult<SnapshotTakeResult> result = await SnapshotSamples.TakeAsync(home, SnapshotSamples.Taken);

        Assert.True(result.Ok, result.Failure?.Message);
        SnapshotTakeResult take = result.Data!;
        Assert.Equal("2026-10-31T060000Z.sqlite", take.Snapshot.Name);
        Assert.Equal(Path.Combine(home, "snapshots", "contoso", "2026-10-31T060000Z.sqlite"), take.Path);
        Assert.Equal(["2026-10-31T060000Z.sqlite"], Directory.GetFiles(Path.GetDirectoryName(take.Path)!).Select(Path.GetFileName));
        Assert.Equal(
            ["users ok 7", "groups ok 6", "mailboxes ok 6", "members ok 14", "userGroups ok 12", "owners ok 5", "teams ok 2", "sites partial 3", "sharing ok 7",
             "oneDrive skipped 0", "grants ok 7", "folderPermissions ok 14", "forwarding ok 3"],
            take.Domains.Select(domain => $"{domain.Name} {domain.Status.ToString().ToLowerInvariant()} {domain.Rows}"));
        // What each domain sent (task 097): Graph requests, Admin API and sidecar cmdlets; the skipped one nothing.
        Assert.Equal(
            ["users 1 0 0", "groups 1 0 0", "mailboxes 0 1 0", "members 2 0 0", "userGroups 0 0 0", "owners 1 0 2", "teams 1 0 0", "sites 2 0 0", "sharing 6 0 0",
             "oneDrive 0 0 0", "grants 0 0 6", "folderPermissions 0 6 6", "forwarding 0 0 3"],
            take.Domains.Select(domain => $"{domain.Name} {domain.GraphRequests} {domain.AdminApiCalls} {domain.SidecarCalls}"));
        Assert.All(take.Domains, domain => Assert.InRange(domain.ElapsedMs, 0, take.ElapsedMs));
        SnapshotDomain sites = take.Domains.Single(domain => domain.Name == "sites");
        Assert.StartsWith("1 could not be read (forbidden 1), for example Communication site: Microsoft Graph answered 403", sites.Note, StringComparison.Ordinal);
        Assert.Equal((1, "forbidden 1"), (sites.ItemsFailed, sites.ItemFailures));
        Assert.Empty(take.Pruned);
        Assert.Empty(take.Snapshot.Failed);

        SnapshotDetail detail = SnapshotStore.Show(home, "contoso", null);
        Assert.Equal(take.Snapshot, detail.Snapshot with { Failed = take.Snapshot.Failed });
        Assert.Empty(detail.Snapshot.Failed);
        Assert.Equal(("contoso", "contoso.onmicrosoft.com", SnapshotSchema.Version), (detail.Profile, detail.Tenant, detail.SchemaVersion));
        Assert.Equal(ProductInfo.Version, detail.ToolVersion);
        Assert.Equal(take.Domains, detail.Domains);
        Assert.Equal(17, detail.Tables["objects"]);
        Assert.Equal(6, detail.Tables["linkGrantees"]);

        await using var connection = new SqliteConnection($"Data Source={take.Path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(Ct);
        // A mailbox is its user's object: Exchange's id of alice's mailbox and Graph's of her account are one object.
        Assert.Equal(["user alice@contoso.com UserMailbox"], Strings(connection, "select kind || ' ' || userPrincipalName || ' ' || mailboxType from objects where name = 'Alice Example'"));
        // The closure names the direct member group a nested member comes through.
        Assert.Equal(
            ["Finance via Finance EU", "Finance EU direct", "Finance Staff via Finance EU", "Project Phoenix direct"],
            Strings(connection, """
                select g.name || ' ' || coalesce('via ' || v.name, 'direct') from userGroups u join objects g on g.key = u.groupKey
                left join objects v on v.key = u.viaKey join objects d on d.key = u.userKey where d.userPrincipalName = 'dave@contoso.com' order by g.name
                """));
        Assert.Equal(
            ["Finance alice graph", "Finance EU carol graph", "Finance Staff carol managedBy", "Finance Staff Organization Management managedBy", "Sales alice managedBy"],
            Strings(connection, """
                select g.name || ' ' || coalesce(substr(o.userPrincipalName, 1, instr(o.userPrincipalName, '@') - 1), o.name) || ' ' || w.source
                from owners w join objects g on g.key = w.groupKey join objects o on o.key = w.ownerKey order by 1 collate nocase
                """));
        Assert.Equal(
            ["Finance drive-finance Finance", "Project Phoenix drive-phoenix Project Phoenix", "Communication site - -"],
            Strings(connection, "select s.name || ' ' || coalesce(s.driveId, '-') || ' ' || coalesce(g.name, '-') from sites s left join objects g on g.key = s.groupKey order by s.groupKey is null, s.name"));
        // The sharing links keep their scope, never a URL; the site's own groups and the inherited grant are left out.
        Assert.Equal(
            ["Budget.xlsx link organization view read", "Budget.xlsx link users edit write", "Budget.xlsx direct - - write", "Reports link anonymous view read", "Reports direct - - read",
             "Roadmap.pptx link users view read", "Roadmap.pptx direct - - read"],
            Strings(connection, "select itemName || ' ' || kind || ' ' || coalesce(scope, '-') || ' ' || coalesce(type, '-') || ' ' || roles from links order by itemName, kind = 'direct', scope"));
        Assert.Equal(
            ["Alice Example user", "Bob Example user", "Dave Example user", "Finance EU group", "Finance Staff group", "Partner Example external"],
            Strings(connection, "select o.name || ' ' || o.kind from linkGrantees g join objects o on o.key = g.principalKey order by o.name"));
        Assert.Equal(
            ["https://graph.microsoft.com/v1.0/drives/drive-finance/root/delta?token=delta-finance-1", "https://graph.microsoft.com/v1.0/drives/drive-phoenix/root/delta?token=delta-phoenix-1"],
            Strings(connection, "select value from manifest where key like 'deltaLink.%' order by key"));
        Assert.Equal(["2026-12-31T00:00:00Z 1"], Strings(connection, "select expires || ' ' || hasPassword from links where expires is not null"));
        Assert.Equal(
            ["Finance full-access Alice Example", "Finance send-as Alice Example", "Finance send-on-behalf Bob Example", "Reception full-access Alice Example",
             "Reception full-access Bob Example", "Reception full-access Finance Staff", "Reception send-as Bob Example"],
            Strings(connection, "select m.name || ' ' || g.right || ' ' || h.name from grants g join objects m on m.key = g.mailboxKey join objects h on h.key = g.holderKey order by 1 collate nocase"));
        Assert.Equal(
            ["Carol Example Calendar Finance Staff Reviewer", "Finance Calendar Alice Example Editor", "Reception Kalender Bob Example Reviewer"],
            Strings(connection, "select m.name || ' ' || f.folder || ' ' || p.name || ' ' || f.rights from folderPermissions f join objects m on m.key = f.mailboxKey join objects p on p.key = f.principalKey order by 1 collate nocase"));
        Assert.Equal(
            ["Bob Example smtp-address bob@fabrikam.com - 1 1", "Carol Example recipient finance@contoso.com Finance 0 0", "Dave Example recipient dave@example.com Dave Private 1 0"],
            Strings(connection, """
                select m.name || ' ' || f.kind || ' ' || f.targetAddress || ' ' || coalesce(t.name, '-') || ' ' || f.external || ' ' || f.keepsCopy
                from forwarding f join objects m on m.key = f.mailboxKey left join objects t on t.key = f.targetKey order by 1 collate nocase
                """));
        Assert.Equal(1L, (long)SnapshotStore.Scalar(connection, "pragma user_version")!);
    }

    [Fact]
    public async Task ASecondTakePrunesByTheRetentionAndLeavesEveryOtherFile()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        DateTimeOffset now = new(2026, 12, 31, 6, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] older =
        [
            now.AddHours(-30), now.AddHours(-40), now.AddDays(-3).AddHours(-1), now.AddDays(-3).AddHours(-2), now.AddDays(-20), now.AddDays(-20).AddHours(-3),
            now.AddDays(-120),
        ];
        foreach (DateTimeOffset taken in older)
        {
            await SnapshotSamples.SeedAsync(home, taken);
        }

        string folder = SnapshotStore.Folder(home, "contoso");
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.sqlite"), "not a snapshot", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "2026-12-30T000000Z.sqlite.partial"), "half a snapshot", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "other.sqlite.partial"), "not ours", Ct);

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(home, now, SnapshotSamples.Profile with { KeepDaily = 14, KeepWeekly = 4 })).Data!;

        Assert.Equal(["2026-12-30T000000Z.sqlite.partial"], take.RemovedPartials);
        // Kept: the two of the last two days, the newest of the 28th (a day of the last fourteen), the newest of the week
        // of the 11th (one of the last four weeks); pruned: the older one of the 28th (whose week's newest is the take),
        // the older one of the 11th, and the one of September.
        Assert.Equal(["2026-09-02T060000Z.sqlite", "2026-12-11T030000Z.sqlite", "2026-12-28T040000Z.sqlite"], take.Pruned);
        Assert.Equal(new SnapshotRetention(14, 4), take.Retention);
        Assert.Equal(
            ["2026-12-11T060000Z.sqlite", "2026-12-28T050000Z.sqlite", "2026-12-29T140000Z.sqlite", "2026-12-30T000000Z.sqlite", "2026-12-31T060000Z.sqlite", "notes.sqlite", "other.sqlite.partial"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        SnapshotList list = SnapshotStore.List(home, "contoso");
        Assert.Equal(5, list.Snapshots.Count);
        Assert.Equal("2026-12-31T060000Z.sqlite", list.Snapshots[0].Name);
        Assert.Equal(0, list.Unreadable);
    }

    [Fact]
    public async Task ATakeOfTheSameSecondNeverReplacesTheSnapshot()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        string file = await SnapshotSamples.SeedAsync(home);
        byte[] before = await File.ReadAllBytesAsync(file, Ct);

        AegisException failure = await Assert.ThrowsAsync<AegisException>(() => SnapshotSamples.TakeAsync(home, SnapshotSamples.Taken));

        Assert.Equal(FailureKind.Forbidden, failure.Failure.Kind);
        Assert.StartsWith("the snapshot 2026-10-31T060000Z.sqlite could not be written", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, Ct));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    [Fact]
    public async Task ADomainThatFailsIsRecordedAndNothingIsPruned()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        string old = await SnapshotSamples.SeedAsync(home, SnapshotSamples.Taken.AddDays(-200));
        // No Admin API: the mailboxes come from the sidecar, and the calendars, which only the Admin API reads, fail.
        var backends = new ExchangeBackends(SidecarFixtures.Session(), null, () => GraphFixtures.Client().Client, () => SidecarFixtures.Session());

        CommandResult<SnapshotTakeResult> result = await SnapshotSamples.TakeAsync(home, SnapshotSamples.Taken, backends: backends);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal("1 of 13 domains could not be read (folderPermissions); the snapshot is kept without them", result.Failure!.Message);
        SnapshotDomain calendars = result.Data!.Domains.Single(domain => domain.Name == "folderPermissions");
        Assert.Equal(SnapshotDomainStatus.Failed, calendars.Status);
        Assert.Equal("service-error: the calendars' permissions need the Exchange Online Admin API, which this run does not have", calendars.Note);
        Assert.Equal(["folderPermissions"], result.Data.Snapshot.Failed);
        Assert.Empty(result.Data.Pruned);
        Assert.True(File.Exists(old));
        Assert.Equal(["folderPermissions"], SnapshotStore.List(home, "contoso").Snapshots[0].Failed);
        Assert.Contains(
            "folderPermissions could not be read in this snapshot; `m365-aegis snapshot show` names the failure",
            SnapshotAccess.ForUser(home, "contoso", "alice@contoso.com", SnapshotSamples.Taken).Warnings);
    }

    [Fact]
    public async Task AnAdminApiThatRefusesTheMailboxesFallsBackAndTheCalendarsFailOneByOne()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        var refusing = new CannedRunner(_ => throw new AegisException(FailureKind.Forbidden, "the Admin API refused"));
        var backends = new ExchangeBackends(SidecarFixtures.Session(), refusing, () => GraphFixtures.Client().Client, () => SidecarFixtures.Session());

        SnapshotTakeResult take = (await SnapshotSamples.TakeAsync(home, SnapshotSamples.Taken, backends: backends)).Data!;

        SnapshotDomain calendars = take.Domains.Single(domain => domain.Name == "folderPermissions");
        Assert.Equal(SnapshotDomainStatus.Partial, calendars.Status);
        Assert.Equal(0, calendars.Rows);
        // Six failed, three named: a note stays short whatever the count.
        Assert.Equal(
            "6 could not be read (forbidden 6), for example alice@contoso.com: the Admin API refused; bob@contoso.com: the Admin API refused; carol@contoso.com: the Admin API refused",
            calendars.Note);
        Assert.Equal(SnapshotDomainStatus.Ok, take.Domains.Single(domain => domain.Name == "mailboxes").Status);
    }

    private static List<string> Strings(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
