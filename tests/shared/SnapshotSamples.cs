using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

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
