using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Tests;

/// <summary>
/// The fixture snapshot (task 087): a take over the recorded Graph and Admin API answers and the fake Exchange Online
/// module, in process, into a temporary home. It is built once per test run and copied into each test's home, never
/// committed (tests/fixtures/README.md, "The snapshot fixture"). Linked into both test projects.
/// </summary>
internal static class SnapshotSamples
{
    /// <summary>When the fixture snapshot is taken: its name is <c>2026-10-31T060000Z.sqlite</c>.</summary>
    internal static readonly DateTimeOffset Taken = new(2026, 10, 31, 6, 0, 0, TimeSpan.Zero);

    /// <summary>The profile of the samples, as <c>config.json</c> holds it.</summary>
    internal static readonly TenantProfile Profile = new("contoso", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

    private static readonly Lazy<Task<byte[]>> Bytes = new(BuildAsync);

    /// <summary>Writes <c>config.json</c> with the samples' profile, the default one, into the home.</summary>
    internal static string WriteProfile(string home)
    {
        new ProfileStore(home).Save(Profile, makeDefault: true);
        return home;
    }

    /// <summary>The backends of a take over the fixtures and the fake module: the sidecar, the Admin API, Graph, more sidecars.</summary>
    internal static ExchangeBackends Backends() => new(
        SidecarFixtures.Session(),
        AdminApiConnection.Fixtures(GraphFixtures.Root),
        () => GraphFixtures.Client().Client,
        () => SidecarFixtures.Session());

    /// <summary>A take into the home at the clock's time, with the samples' profile (or another).</summary>
    internal static async Task<CommandResult<SnapshotTakeResult>> TakeAsync(string home, DateTimeOffset now, TenantProfile? profile = null, ExchangeBackends? backends = null)
    {
        await using ExchangeBackends used = backends ?? Backends();
        return await SnapshotTake.RunAsync(used, new SnapshotTakeOptions(home, profile ?? Profile) { Clock = new FixedClock(now) }, CancellationToken.None);
    }

    /// <summary>Copies the fixture snapshot into the home's folder of the samples' profile under the name of <paramref name="taken"/>; the file.</summary>
    internal static async Task<string> SeedAsync(string home, DateTimeOffset? taken = null)
    {
        string folder = SnapshotStore.Folder(home, Profile.Name);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, SnapshotStore.NameFor(taken ?? Taken));
        await File.WriteAllBytesAsync(file, await Bytes.Value);
        return file;
    }

    /// <summary>When the changed snapshot of <see cref="SeedChangedAsync"/> is taken: a day after the fixture's.</summary>
    internal static readonly DateTimeOffset ChangedTaken = Taken.AddDays(1);

    /// <summary>
    /// The fixture snapshot a day later (task 098), copied under the name of <see cref="ChangedTaken"/> and changed in
    /// place: one row removed and one added in each domain <c>snapshot diff</c> compares, a group renamed, and every
    /// integer key moved by 100, so a diff that matched rows by key or by display name would show changes it must not.
    /// And a new group, Audit (task 114): bob is its member and it is a member of Finance Staff, so bob gains Finance
    /// Staff through Audit, and with it what Finance Staff holds; <c>userGroups</c> follows the members as a take
    /// computes it.
    /// </summary>
    internal static async Task<string> SeedChangedAsync(string home)
    {
        string file = await SeedAsync(home, ChangedTaken);
        Execute(file, """
            update objects set name = 'Finance Team' where objectId = '00000000-0000-0000-0000-000000000031';
            delete from members where groupKey = 9 and memberKey = 14;
            insert into members values (10, 2);
            insert into userGroups values (2, 10, null);
            insert into objects values (18, '00000000-0000-0000-0000-000000000037', 'group', null, 'Audit', 'audit@contoso.com', 'Security', null, 0);
            insert into members values (18, 2), (11, 18);
            insert into userGroups values (2, 18, null), (2, 11, 18);
            delete from owners where groupKey = 9 and ownerKey = 3;
            insert into owners values (10, 4, 'graph');
            delete from links where key = 1;
            insert into linkGrantees values (6, 2);
            delete from grants where mailboxKey = 7 and holderKey = 11;
            insert into grants values (6, 'full-access', 4);
            update folderPermissions set rights = 'Reviewer' where mailboxKey = 3 and principalName = 'Default';
            delete from forwarding where mailboxKey = 2;
            insert into forwarding values (1, 'recipient', 'Carol Example', 'carol@contoso.com', 3, 0, 1);
            update objects set key = key + 100;
            update members set groupKey = groupKey + 100, memberKey = memberKey + 100;
            update userGroups set userKey = userKey + 100, groupKey = groupKey + 100, viaKey = viaKey + 100;
            update owners set groupKey = groupKey + 100, ownerKey = ownerKey + 100;
            update sites set key = key + 100, groupKey = groupKey + 100;
            update teams set groupKey = groupKey + 100;
            update links set key = key + 100, siteKey = siteKey + 100;
            update linkGrantees set linkKey = linkKey + 100, principalKey = principalKey + 100;
            update grants set mailboxKey = mailboxKey + 100, holderKey = holderKey + 100;
            update folderPermissions set mailboxKey = mailboxKey + 100, principalKey = principalKey + 100;
            update forwarding set mailboxKey = mailboxKey + 100, targetKey = targetKey + 100;
            """);
        return file;
    }

    /// <summary>Runs statements against a seeded snapshot file (a test's change of its rows or its manifest).</summary>
    internal static void Execute(string file, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task<byte[]> BuildAsync()
    {
        string home = PlanSamples.TempHome();
        CommandResult<SnapshotTakeResult> result = await TakeAsync(home, Taken);
        return await File.ReadAllBytesAsync(result.Data!.Path);
    }

    /// <summary>A clock that stands still.</summary>
    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
