using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Snapshots;

/// <summary>
/// The snapshots under the home folder (D-005, D-011, task 087): <c>&lt;home&gt;\snapshots\&lt;profile&gt;\</c>, one folder
/// per profile so two tenants never mix, one file per snapshot named by the UTC time of its take,
/// <c>yyyy-MM-ddTHHmmssZ.sqlite</c>. A take writes <c>&lt;name&gt;.sqlite.partial</c> and renames it when it is complete,
/// so a reader never opens half a snapshot. A file is read only when its name is a snapshot's and its schema is
/// <see cref="SnapshotSchema.Version"/>; every other file of the folder is left alone.
/// </summary>
public static class SnapshotStore
{
    /// <summary>The extension of a snapshot.</summary>
    public const string Extension = ".sqlite";

    /// <summary>The extension of a snapshot while it is written.</summary>
    public const string PartialExtension = ".sqlite.partial";

    /// <summary>The command that takes a snapshot, which every answer without one names.</summary>
    public const string TakeHint = "run `m365-aegis snapshot take` first (docs/usage.md, \"Snapshots\")";

    private const string NameFormat = "yyyy-MM-dd'T'HHmmss'Z'";

    /// <summary>
    /// The snapshot folder of a profile under the home folder, not created. A profile's name is the folder's, so a name
    /// a Windows folder cannot carry is refused.
    /// </summary>
    /// <exception cref="AegisException">A usage error for a name that cannot be a folder.</exception>
    public static string Folder(string home, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Trim().Length == 0 || profile is "." or ".." || profile.EndsWith('.') || profile.EndsWith(' ')
            || profile.Any(character => character < ' ' || "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal)))
        {
            throw new AegisException(
                FailureKind.Usage,
                $"the profile name '{profile}' cannot name a folder for its snapshots",
                "give the profile a name of letters, digits, dashes and dots (`m365-aegis login --profile <name>`)");
        }

        return Path.Combine(home, "snapshots", profile);
    }

    /// <summary>The profile a snapshot command reads: <c>--profile</c> when given, else the default one of <c>config.json</c>.</summary>
    /// <exception cref="AegisException">No profile given and no default one in the file.</exception>
    public static string ProfileName(string home, string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? new ProfileStore(home).Resolve(null).Name : profile;

    /// <summary>The file name of a snapshot taken at <paramref name="taken"/>.</summary>
    public static string NameFor(DateTimeOffset taken) => taken.UtcDateTime.ToString(NameFormat, CultureInfo.InvariantCulture) + Extension;

    /// <summary>The time of a snapshot's name, or null when the name is not a snapshot's.</summary>
    public static DateTimeOffset? TakenOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.EndsWith(Extension, StringComparison.Ordinal)
            && DateTime.TryParseExact(fileName[..^Extension.Length], NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime taken)
                ? new DateTimeOffset(taken, TimeSpan.Zero)
                : null;
    }

    /// <summary>
    /// Removes the <c>.partial</c> files a take left when it did not finish (D-011: the next take removes them); a file
    /// whose name is not a snapshot's is left alone. Returns the names removed.
    /// </summary>
    internal static IReadOnlyList<string> RemovePartials(string folder)
    {
        var removed = new List<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*" + PartialExtension).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (TakenOf(name[..^".partial".Length]) is not null)
            {
                File.Delete(file);
                removed.Add(name);
            }
        }

        return removed;
    }

    /// <summary><c>snapshot list</c>: the profile's snapshots, newest first; an unreadable one is counted, not listed.</summary>
    public static SnapshotList List(string home, string profile)
    {
        string folder = Folder(home, profile);
        var snapshots = new List<SnapshotInfo>();
        int unreadable = 0;
        foreach (string file in Files(folder))
        {
            if (TryRead(file) is { } detail)
            {
                snapshots.Add(detail.Snapshot);
            }
            else
            {
                unreadable++;
            }
        }

        return new SnapshotList(profile, folder, snapshots) { Unreadable = unreadable };
    }

    /// <summary><c>snapshot show [&lt;snapshot&gt;]</c>: one snapshot's manifest and table counts, the newest readable one by default.</summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when there is no such snapshot.</exception>
    public static SnapshotDetail Show(string home, string profile, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Newest(home, profile).Detail;
        }

        string folder = Folder(home, profile);
        string file = Path.Combine(folder, name.EndsWith(Extension, StringComparison.Ordinal) ? name : name + Extension);
        return TakenOf(Path.GetFileName(file)) is not null && File.Exists(file) && TryRead(file) is { } detail
            ? detail
            : throw new AegisException(
                FailureKind.NotFound,
                $"no readable snapshot '{name}' of the profile '{profile}'",
                "`m365-aegis snapshot list` shows the snapshots by name");
    }

    /// <summary>The newest readable snapshot of a profile: its file and its manifest.</summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when the profile has none.</exception>
    internal static (string File, SnapshotDetail Detail) Newest(string home, string profile)
    {
        foreach (string file in Files(Folder(home, profile)))
        {
            if (TryRead(file) is { } detail)
            {
                return (file, detail);
            }
        }

        throw new AegisException(FailureKind.NotFound, $"no snapshot of the profile '{profile}' yet", TakeHint);
    }

    /// <summary>A snapshot file opened read-only, its schema checked; null when it is not one of this version.</summary>
    internal static SqliteConnection? OpenRead(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            if (Convert.ToInt64(Scalar(connection, "pragma user_version"), CultureInfo.InvariantCulture) == SnapshotSchema.Version)
            {
                return connection;
            }
        }
        catch (SqliteException)
        {
            // Not a database at all: a damaged or foreign file under a snapshot's name.
        }

        connection.Dispose();
        return null;
    }

    /// <summary>A snapshot's manifest and the rows of each table; null when the file is not a snapshot of this version.</summary>
    internal static SnapshotDetail? TryRead(string file)
    {
        using SqliteConnection? connection = OpenRead(file);
        if (connection is null)
        {
            return null;
        }

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select key, value from manifest";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                manifest[reader.GetString(0)] = reader.GetString(1);
            }
        }

        List<SnapshotDomain> domains = manifest
            .Where(pair => pair.Key.StartsWith(DomainPrefix, StringComparison.Ordinal))
            .Select(pair => JsonSerializer.Deserialize<SnapshotDomain>(pair.Value, EnvelopeJson.Options)!)
            .OrderBy(domain => int.Parse(manifest[OrderPrefix + domain.Name], CultureInfo.InvariantCulture))
            .ToList();
        var tables = SnapshotSchema.Tables.ToDictionary(
            table => table,
            table => Convert.ToInt64(Scalar(connection, $"select count(*) from {table}"), CultureInfo.InvariantCulture),
            StringComparer.Ordinal);
        string name = Path.GetFileName(file);
        var info = new SnapshotInfo(
            name,
            TakenOf(name)!.Value,
            new FileInfo(file).Length,
            domains.Where(domain => domain.Status == SnapshotDomainStatus.Failed).Select(domain => domain.Name).ToList());
        return new SnapshotDetail(
            info,
            manifest["profile"],
            manifest["tenant"],
            manifest["toolVersion"],
            SnapshotSchema.Version,
            long.Parse(manifest["elapsedMs"], CultureInfo.InvariantCulture),
            domains,
            tables);
    }

    /// <summary>The manifest's key of a domain's record, its JSON.</summary>
    internal const string DomainPrefix = "domain.";

    /// <summary>The manifest's key of a domain's place in the take's order.</summary>
    internal const string OrderPrefix = "order.";

    /// <summary>
    /// The prune after a take that succeeded (D-011): every snapshot the retention does not keep goes, never the newest
    /// and never a file whose name is not a snapshot's. Returns the names removed, oldest first, and the files the file
    /// system refused to remove (one in use by a reader), which stay for the next prune.
    /// </summary>
    internal static (IReadOnlyList<string> Pruned, IReadOnlyList<string> Refused) Prune(string folder, SnapshotRetention retention, DateTimeOffset now, Action<string> delete)
    {
        List<(string File, DateTimeOffset Taken)> snapshots = Directory.EnumerateFiles(folder, "*" + Extension)
            .Select(file => (File: file, Taken: TakenOf(Path.GetFileName(file))))
            .Where(snapshot => snapshot.Taken is not null)
            .Select(snapshot => (snapshot.File, snapshot.Taken!.Value))
            .OrderBy(snapshot => snapshot.Item2)
            .ToList();
        HashSet<DateTimeOffset> keep = Keep(snapshots.Select(snapshot => snapshot.Item2).ToList(), now, retention);
        var pruned = new List<string>();
        var refused = new List<string>();
        foreach ((string file, DateTimeOffset taken) in snapshots.Where(snapshot => !keep.Contains(snapshot.Item2)))
        {
            string name = Path.GetFileName(file);
            try
            {
                delete(file);
                pruned.Add(name);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{name} could not be pruned ({exception.Message}); the next take tries again");
            }
        }

        return (pruned, refused);
    }

    /// <summary>
    /// The times the retention keeps (D-011): the newest; every one of the last <see cref="SnapshotRetention.KeepAllDays"/>
    /// days; the newest of each of the last <see cref="SnapshotRetention.KeepDaily"/> UTC days, today included; the newest
    /// of each of the last <see cref="SnapshotRetention.KeepWeekly"/> ISO weeks, this week included.
    /// </summary>
    internal static HashSet<DateTimeOffset> Keep(IReadOnlyList<DateTimeOffset> taken, DateTimeOffset now, SnapshotRetention retention)
    {
        var keep = new HashSet<DateTimeOffset>();
        if (taken.Count == 0)
        {
            return keep;
        }

        keep.Add(taken.Max());
        keep.UnionWith(taken.Where(moment => now - moment <= TimeSpan.FromDays(SnapshotRetention.KeepAllDays)));
        DateTime today = now.UtcDateTime.Date;
        keep.UnionWith(taken
            .GroupBy(moment => moment.UtcDateTime.Date)
            .Where(day => (today - day.Key).TotalDays < retention.KeepDaily)
            .Select(day => day.Max()));
        keep.UnionWith(taken
            .GroupBy(moment => Monday(moment.UtcDateTime.Date))
            .Where(week => (Monday(today) - week.Key).TotalDays / 7 < retention.KeepWeekly)
            .Select(week => week.Max()));
        return keep;
    }

    /// <summary>The Monday that starts a day's ISO week.</summary>
    private static DateTime Monday(DateTime day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The snapshot files of a folder, newest first; none when the folder does not exist.</summary>
    private static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*" + Extension)
                .Where(file => TakenOf(Path.GetFileName(file)) is not null)
                .OrderDescending(StringComparer.Ordinal)
            : [];

    internal static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
