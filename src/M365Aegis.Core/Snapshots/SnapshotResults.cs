namespace M365Aegis.Core.Snapshots;

/// <summary>How one domain of a snapshot was read (D-011: the manifest says what failed).</summary>
public enum SnapshotDomainStatus
{
    /// <summary>Read whole.</summary>
    Ok,

    /// <summary>Read, but some of its items could not be (a mailbox, a site); the note names them.</summary>
    Partial,

    /// <summary>Not read: the note has the service's failure. The rest of the snapshot stays usable.</summary>
    Failed,

    /// <summary>Not read on purpose: the note says why (a consent the tool does not ask for).</summary>
    Skipped,
}

/// <summary>One domain of a snapshot as its manifest records it.</summary>
/// <param name="Name">The domain: <c>users</c>, <c>groups</c>, <c>members</c>, …</param>
/// <param name="Status">How it was read.</param>
/// <param name="Read">Which read answered it.</param>
/// <param name="Rows">The rows it wrote.</param>
/// <param name="Note">What failed or why it was skipped; null when it was read whole.</param>
public sealed record SnapshotDomain(string Name, SnapshotDomainStatus Status, string Read, int Rows, string? Note = null);

/// <summary>One snapshot file as <c>snapshot list</c> shows it.</summary>
/// <param name="Name">The file's name, <c>yyyy-MM-ddTHHmmssZ.sqlite</c>, the UTC time of the take.</param>
/// <param name="Taken">When the take started.</param>
/// <param name="Bytes">The file's size.</param>
/// <param name="Failed">The domains that failed, by name; empty for a snapshot read whole.</param>
public sealed record SnapshotInfo(string Name, DateTimeOffset Taken, long Bytes, IReadOnlyList<string> Failed);

/// <summary>The answer of <c>snapshot list</c>: the snapshots of one profile, newest first.</summary>
/// <param name="Profile">The tenant profile.</param>
/// <param name="Folder">The profile's snapshot folder.</param>
/// <param name="Snapshots">The snapshots, newest first.</param>
public sealed record SnapshotList(string Profile, string Folder, IReadOnlyList<SnapshotInfo> Snapshots)
{
    /// <summary>The size of every snapshot listed, in bytes.</summary>
    public long Bytes => Snapshots.Sum(snapshot => snapshot.Bytes);

    /// <summary>Files named like a snapshot that cannot be read as one (another schema version, a damaged file).</summary>
    public int Unreadable { get; init; }
}

/// <summary>The answer of <c>snapshot show</c>: a snapshot's manifest and the rows of each table.</summary>
/// <param name="Snapshot">The snapshot.</param>
/// <param name="Profile">The tenant profile it was taken for.</param>
/// <param name="Tenant">The profile's tenant.</param>
/// <param name="ToolVersion">The version of the tool that took it.</param>
/// <param name="SchemaVersion">The schema's version (<see cref="SnapshotSchema.Version"/>).</param>
/// <param name="ElapsedMs">How long the reads took.</param>
/// <param name="Domains">Each domain with its read and its rows.</param>
/// <param name="Tables">The rows of each table.</param>
public sealed record SnapshotDetail(
    SnapshotInfo Snapshot,
    string Profile,
    string Tenant,
    string ToolVersion,
    int SchemaVersion,
    long ElapsedMs,
    IReadOnlyList<SnapshotDomain> Domains,
    IReadOnlyDictionary<string, long> Tables);

/// <summary>What the retention keeps (D-011); <c>keepDaily</c> and <c>keepWeekly</c> of a profile in <c>config.json</c> override the defaults.</summary>
/// <param name="KeepDaily">The days for which the newest snapshot of each day is kept.</param>
/// <param name="KeepWeekly">The ISO weeks for which the newest snapshot of each week is kept.</param>
public sealed record SnapshotRetention(int KeepDaily = 14, int KeepWeekly = 8)
{
    /// <summary>Every snapshot of the last this many days is kept.</summary>
    public const int KeepAllDays = 2;
}

/// <summary>The answer of <c>snapshot take</c>.</summary>
/// <param name="Snapshot">The snapshot written.</param>
/// <param name="Path">Its file.</param>
/// <param name="ElapsedMs">How long the reads took.</param>
/// <param name="Domains">Each domain with its read and its rows.</param>
/// <param name="Retention">The retention the prune applied.</param>
/// <param name="Pruned">The snapshots the prune removed, oldest first; empty after a take with a failed domain, which prunes nothing.</param>
public sealed record SnapshotTakeResult(
    SnapshotInfo Snapshot,
    string Path,
    long ElapsedMs,
    IReadOnlyList<SnapshotDomain> Domains,
    SnapshotRetention Retention,
    IReadOnlyList<string> Pruned)
{
    /// <summary>The <c>.partial</c> files of an earlier take that did not finish, removed before this one started.</summary>
    public IReadOnlyList<string> RemovedPartials { get; init; } = [];

    /// <summary>What the take could not do besides a domain: a snapshot the prune could not remove.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
