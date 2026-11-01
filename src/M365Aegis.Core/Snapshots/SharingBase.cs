using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Output;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Snapshots;

/// <summary>
/// What an incremental sharing read starts from (task 099): the newest snapshot of the profile whose sharing domain was
/// read, each drive's delta link it stored, and the <c>links</c> and <c>linkGrantees</c> of those drives. A take since
/// task 099 stores a drive's link only when the drive was read whole (<see cref="WholeDrives"/> in its manifest), so a
/// partial sharing domain still serves its whole drives; a snapshot taken before (no marker) serves only when its
/// sharing domain was read whole, since it cannot tell which drive failed.
/// </summary>
internal sealed class SharingBase
{
    /// <summary>The manifest's key of a drive's delta link, followed by the drive's id.</summary>
    internal const string DeltaLinkPrefix = "deltaLink.";

    /// <summary>The manifest's key that says a drive's delta link is stored only after a whole read of the drive.</summary>
    internal const string WholeDrives = "deltaLinks";

    /// <summary>The value of <see cref="WholeDrives"/>.</summary>
    internal const string WholeDrivesValue = "whole-drives";

    /// <summary>
    /// The manifest's key of the time of the last take that read every drive whole (task 116): the take's own when it
    /// did, else carried from the snapshot it started from.
    /// </summary>
    internal const string WholeReadKey = "sharingWholeRead";

    /// <summary>
    /// The manifest's key of why a take read every drive whole (task 116): <c>full</c> (<c>--full</c>), <c>no-base</c>
    /// (no snapshot to start from), <c>age</c> (the last whole read is <c>fullSharingDays</c> or more days old) or
    /// <c>unknown</c> (the base does not say when its drives were last read whole); absent in an incremental take.
    /// </summary>
    internal const string WholeReasonKey = "sharingWholeReason";

    private SharingBase(string name, DateTimeOffset? wholeRead, Dictionary<string, string> deltaLinks, Dictionary<string, List<BaseLink>> links)
    {
        Name = name;
        WholeRead = wholeRead;
        DeltaLinks = deltaLinks;
        Links = links;
    }

    /// <summary>The snapshot's file name.</summary>
    internal string Name { get; }

    /// <summary>When every drive was last read whole, as the snapshot's manifest says; null in a snapshot taken before task 116.</summary>
    internal DateTimeOffset? WholeRead { get; }

    /// <summary>Each drive's delta link, by the drive's id.</summary>
    internal IReadOnlyDictionary<string, string> DeltaLinks { get; }

    /// <summary>The rows of each drive that has a delta link, by the drive's id.</summary>
    internal IReadOnlyDictionary<string, List<BaseLink>> Links { get; }

    /// <summary>The newest snapshot of the folder an incremental read can start from; null when there is none.</summary>
    internal static SharingBase? Load(string folder)
    {
        foreach (string file in SnapshotStore.Files(folder))
        {
            using SqliteConnection? connection = SnapshotStore.OpenRead(file);
            if (connection is null)
            {
                continue;
            }

            Dictionary<string, string> manifest = Manifest(connection);
            SnapshotDomainStatus sharing = JsonSerializer.Deserialize<SnapshotDomain>(manifest[SnapshotStore.DomainPrefix + "sharing"], EnvelopeJson.Options)!.Status;
            bool whole = manifest.GetValueOrDefault(WholeDrives) == WholeDrivesValue;
            if (sharing == SnapshotDomainStatus.Ok || (sharing == SnapshotDomainStatus.Partial && whole))
            {
                Dictionary<string, string> deltaLinks = manifest
                    .Where(pair => pair.Key.StartsWith(DeltaLinkPrefix, StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key[DeltaLinkPrefix.Length..], pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                DateTimeOffset? wholeRead = manifest.TryGetValue(WholeReadKey, out string? at)
                    ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                    : null;
                return new SharingBase(Path.GetFileName(file), wholeRead, deltaLinks, LinksOf(connection, deltaLinks));
            }
        }

        return null;
    }

    private static Dictionary<string, string> Manifest(SqliteConnection connection)
    {
        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select key, value from manifest";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            manifest[reader.GetString(0)] = reader.GetString(1);
        }

        return manifest;
    }

    /// <summary>The rows of the drives with a delta link, each with its grantees as the objects they were.</summary>
    private static Dictionary<string, List<BaseLink>> LinksOf(SqliteConnection connection, Dictionary<string, string> deltaLinks)
    {
        var grantees = new Dictionary<long, List<BaseGrantee>>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select g.linkKey, o.kind, o.objectId, o.name, o.mail from linkGrantees g join objects o on o.key = g.principalKey order by g.rowid";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long link = reader.GetInt64(0);
                if (!grantees.TryGetValue(link, out List<BaseGrantee>? list))
                {
                    list = [];
                    grantees[link] = list;
                }

                list.Add(new BaseGrantee(reader.GetString(1), Text(reader, 2), Text(reader, 3), Text(reader, 4)));
            }
        }

        var links = new Dictionary<string, List<BaseLink>>(StringComparer.OrdinalIgnoreCase);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                select s.driveId, l.key, l.itemId, l.itemName, l.itemPath, l.kind, l.scope, l.type, l.roles, l.expires, l.hasPassword
                from links l join sites s on s.key = l.siteKey where s.driveId is not null order by l.key
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string drive = reader.GetString(0);
                if (!deltaLinks.ContainsKey(drive))
                {
                    continue;
                }

                if (!links.TryGetValue(drive, out List<BaseLink>? list))
                {
                    list = [];
                    links[drive] = list;
                }

                list.Add(new BaseLink(
                    reader.GetString(2),
                    Text(reader, 3),
                    Text(reader, 4),
                    reader.GetString(5),
                    Text(reader, 6),
                    Text(reader, 7),
                    Text(reader, 8),
                    Text(reader, 9) is { } expires ? DateTimeOffset.Parse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null,
                    reader.IsDBNull(10) ? null : reader.GetInt64(10) == 1,
                    grantees.GetValueOrDefault(reader.GetInt64(1)) ?? []));
            }
        }

        return links;
    }

    private static string? Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
}

/// <summary>A <c>links</c> row of the previous snapshot, with its grantees.</summary>
internal sealed record BaseLink(
    string ItemId,
    string? ItemName,
    string? ItemPath,
    string Kind,
    string? Scope,
    string? Type,
    string? Roles,
    DateTimeOffset? Expires,
    bool? HasPassword,
    IReadOnlyList<BaseGrantee> Grantees);

/// <summary>A grantee of a previous <c>links</c> row as its object was: its kind, object id, name and address.</summary>
internal sealed record BaseGrantee(string Kind, string? ObjectId, string? Name, string? Mail);
