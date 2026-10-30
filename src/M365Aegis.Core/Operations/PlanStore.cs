using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>Who applied a plan, for its record: the program, the profile, the arguments it was given.</summary>
/// <param name="Source">The program that applied it: <c>command line</c>, later the window.</param>
/// <param name="Profile">The tenant profile (<c>--profile</c>), or null for the default one.</param>
/// <param name="Arguments">The arguments of the command line, as given; empty for the window.</param>
public sealed record PlanRecordContext(string Source, string? Profile, IReadOnlyList<string> Arguments)
{
    /// <summary>The CSV file the changes came from (<c>--from-csv</c>), kept whole so a resume replays it; null for the arguments.</summary>
    public PlanRecordInput? Input { get; init; }
}

/// <summary>The file a batch came from, as the record keeps it (task 062).</summary>
/// <param name="File">The file's full path when the plan was applied.</param>
/// <param name="Content">The file's text then: a resume reads the batch from it, not from the file, which may have changed.</param>
public sealed record PlanRecordInput(string File, string Content);

/// <summary>A plan record as the file holds it.</summary>
/// <param name="Version">
/// The record's format: 2 since task 065, whose changes carry their row's place (<see cref="PlanChange.Index"/>) and
/// their object ids (<see cref="PlanChange.Key"/>); 1 before, which still reads.
/// </param>
/// <param name="Source">The program that applied the plan.</param>
/// <param name="Profile">The tenant profile, or null for the default one.</param>
/// <param name="Arguments">The arguments of the command line.</param>
/// <param name="Plan">The plan, with each change's result once the apply ended.</param>
public sealed record PlanRecord(int Version, string Source, string? Profile, IReadOnlyList<string> Arguments, Plan Plan)
{
    /// <summary>The CSV file of a batch (task 062); null for a plan from the arguments, and in a record written before task 062.</summary>
    public PlanRecordInput? Input { get; init; }
}

/// <summary>One plan record as <c>plan list</c> shows it (task 060).</summary>
/// <param name="Name">The record's file name, which <c>plan show</c> and <c>--resume</c> take.</param>
/// <param name="Operation">The write (<c>group-members-add</c>).</param>
/// <param name="Mode">Where the plan stands: <c>applied</c>, or <c>applying</c> for an apply that was cut short.</param>
/// <param name="Title">The plan's one line.</param>
/// <param name="Changes">How many changes the plan had.</param>
/// <param name="Applied">How many of them were applied.</param>
/// <param name="Failed">How many failed.</param>
/// <param name="Skipped">How many were not sent after a failure that stopped the apply.</param>
public sealed record PlanRecordSummary(string Name, string Operation, PlanMode Mode, string Title, int Changes, int Applied, int Failed, int Skipped)
{
    /// <summary>When the apply started.</summary>
    public DateTimeOffset? Started { get; init; }

    /// <summary>When the apply ended; null for one that was cut short.</summary>
    public DateTimeOffset? Finished { get; init; }

    /// <summary>The tenant profile, or null for the default one.</summary>
    public string? Profile { get; init; }

    /// <summary>The record this apply resumed (task 062), or null.</summary>
    public string? Resumes { get; init; }
}

/// <summary>The answer of <c>plan list</c> (task 060): the records under the home folder's <c>plans</c> folder, newest first.</summary>
/// <param name="Folder">The folder of the records.</param>
/// <param name="Records">The records, newest first.</param>
public sealed record PlanRecordList(string Folder, IReadOnlyList<PlanRecordSummary> Records)
{
    /// <summary>Files in the folder that are not plan records (a broken or foreign file), left out of the list.</summary>
    public int Unreadable { get; init; }

    /// <summary>How many records the folder holds (task 066).</summary>
    public int Count => Records.Count;

    /// <summary>The size of every file in the folder, the records and any other, in bytes (task 066): what <c>plan prune</c> frees.</summary>
    public long Bytes { get; init; }
}

/// <summary>
/// The plan records under the home folder's <c>plans</c> folder (D-005, D-006, task 051): one JSON file per applied
/// plan, <c>yyyyMMddTHHmmss.fffZ-&lt;operation&gt;.json</c> (UTC, so the names sort by time), written when the apply
/// starts (<see cref="PlanMode.Applying"/>, no results) and again when it ends (each change's result, the times). A
/// dry run and a refused plan write nothing. The records stay on the machine, never in the repo. <see cref="List"/> and
/// <see cref="Open"/> read them back for <c>plan list</c>, <c>plan show</c> (task 060) and <c>--resume</c> (task 062).
/// </summary>
public sealed class PlanStore
{
    /// <summary>The format of the records this build writes (task 065): 2, the changes with their rows' places and object ids.</summary>
    public const int RecordVersion = 2;

    private static readonly JsonSerializerOptions RecordOptions = new(EnvelopeJson.Options) { WriteIndented = true };

    private readonly PlanRecordContext _context;

    /// <summary>A store under a home folder.</summary>
    /// <param name="home">The home folder (<see cref="AegisHome.Resolve"/>).</param>
    /// <param name="context">Who applies the plans.</param>
    /// <param name="clock">The clock of the timestamps; the system's by default.</param>
    public PlanStore(string home, PlanRecordContext context, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(context);
        Folder = Path.Combine(home, "plans");
        _context = context;
        Clock = clock ?? TimeProvider.System;
    }

    /// <summary>The store of the command line: the home folder of <see cref="AegisHome"/>.</summary>
    public static PlanStore ForCommandLine(string? profile, IReadOnlyList<string> arguments, PlanRecordInput? input = null) =>
        new(AegisHome.Resolve(), new PlanRecordContext("command line", profile, arguments) { Input = input });

    /// <summary>The folder of the records.</summary>
    public string Folder { get; }

    /// <summary>The clock the apply's times come from.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Writes the record of a plan whose apply starts and returns its file; a name taken already gets a <c>-2</c>, <c>-3</c>, ….</summary>
    internal string Begin(Plan plan)
    {
        // The home first, with its access list (task 075), then the plans folder under it.
        AegisHome.CreateAbove(Folder);
        Directory.CreateDirectory(Folder);
        DateTimeOffset started = plan.Started ?? Clock.GetUtcNow();
        string stem = string.Create(CultureInfo.InvariantCulture, $"{started.UtcDateTime:yyyyMMdd'T'HHmmss'.'fff'Z'}-{plan.Operation}");
        for (int attempt = 1; ; attempt++)
        {
            string path = Path.Combine(Folder, attempt == 1 ? $"{stem}.json" : string.Create(CultureInfo.InvariantCulture, $"{stem}-{attempt}.json"));
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, Record(plan with { Record = path }), RecordOptions);
                return path;
            }
            catch (IOException) when (File.Exists(path) && attempt < 100)
            {
                // Another apply of the same operation in the same millisecond: the next name.
            }
        }
    }

    /// <summary>Writes the record again with the apply's results, through a temporary file so a reader never meets half of it.</summary>
    internal void Finish(string path, Plan plan)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Record(plan), RecordOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads a record back (the tests, <c>plan list</c>, <c>plan show</c>, <c>--resume</c>).</summary>
    public static PlanRecord? Read(string path) => JsonSerializer.Deserialize<PlanRecord>(File.ReadAllText(path), RecordOptions);

    /// <summary>
    /// The records under a home folder's <c>plans</c> folder, newest first (task 060); a file that is not a plan record
    /// is counted, not listed. An empty list when the folder does not exist yet.
    /// </summary>
    /// <param name="home">The home folder (<see cref="AegisHome.Resolve"/>).</param>
    public static PlanRecordList List(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        string folder = Path.Combine(home, "plans");
        var records = new List<PlanRecordSummary>();
        int unreadable = 0;
        long bytes = 0;
        if (Directory.Exists(folder))
        {
            bytes = new DirectoryInfo(folder).EnumerateFiles().Sum(file => file.Length);
            foreach (string file in Directory.EnumerateFiles(folder).Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                if (TryRead(file) is { } record)
                {
                    records.Add(Summary(Path.GetFileName(file), record));
                }
                else
                {
                    unreadable++;
                }
            }
        }

        List<PlanRecordSummary> sorted = records
            .OrderByDescending(record => record.Started ?? DateTimeOffset.MinValue)
            .ThenByDescending(record => record.Name, StringComparer.Ordinal)
            .ToList();
        return new PlanRecordList(folder, sorted) { Unreadable = unreadable, Bytes = bytes };
    }

    /// <summary>
    /// One record by its file name under the home folder's <c>plans</c> folder (with or without <c>.json</c>), or by its
    /// path (task 060).
    /// </summary>
    /// <param name="home">The home folder.</param>
    /// <param name="record">The record's name, as <c>plan list</c> shows it, or a path to the file.</param>
    /// <exception cref="AegisException">No such record (<see cref="FailureKind.NotFound"/>), or a file that is not a plan record (<see cref="FailureKind.Usage"/>).</exception>
    public static (string Path, PlanRecord Record) Open(string home, string record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        string folder = Path.Combine(home, "plans");
        if (string.IsNullOrWhiteSpace(record))
        {
            throw new AegisException(FailureKind.Usage, "no plan record was named", "give a record's name as `m365-aegis plan list` shows it, or its path");
        }

        string given = record.Trim();
        string? path = Candidates(folder, given).FirstOrDefault(File.Exists);
        if (path is null)
        {
            throw new AegisException(
                FailureKind.NotFound,
                $"no plan record '{given}' under {folder}",
                "`m365-aegis plan list` names the records of this home folder");
        }

        return (path, TryRead(path) ?? throw new AegisException(
            FailureKind.Usage,
            $"'{Path.GetFileName(path)}' is not a plan record",
            "a plan record is a file an apply wrote under the home folder's plans folder; `m365-aegis plan list` names them"));
    }

    /// <summary>A record by a path given as is, else by its name in the folder, with or without its extension.</summary>
    private static IEnumerable<string> Candidates(string folder, string given)
    {
        bool name = given.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
        if (!name || Path.IsPathRooted(given))
        {
            yield return Path.GetFullPath(given);
            yield break;
        }

        yield return Path.Combine(folder, given);
        if (!given.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(folder, given + ".json");
        }
    }

    /// <summary>A record read back, or null for a file that cannot be read as one.</summary>
    internal static PlanRecord? TryRead(string path)
    {
        try
        {
            PlanRecord? record = Read(path);
            return record?.Plan is { Operation: not null, Changes: not null } ? record : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static PlanRecordSummary Summary(string name, PlanRecord record)
    {
        IReadOnlyList<PlanChange> changes = record.Plan.Changes;
        return new PlanRecordSummary(
            name,
            record.Plan.Operation,
            record.Plan.Mode,
            record.Plan.Title,
            changes.Count,
            changes.Count(change => change.Result?.Status == ChangeStatus.Applied),
            changes.Count(change => change.Result?.Status == ChangeStatus.Failed),
            changes.Count(change => change.Result?.Status == ChangeStatus.Skipped))
        {
            Started = record.Plan.Started,
            Finished = record.Plan.Finished,
            Profile = record.Profile,
            Resumes = record.Plan.Resumes is { } resumed ? Path.GetFileName(resumed) : null,
        };
    }

    private PlanRecord Record(Plan plan) => new(RecordVersion, _context.Source, _context.Profile, _context.Arguments, plan) { Input = _context.Input };
}
