using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>Who applied a plan, for its record: the program, the profile, the arguments it was given.</summary>
/// <param name="Source">The program that applied it: <c>command line</c>, later the window.</param>
/// <param name="Profile">The tenant profile (<c>--profile</c>), or null for the default one.</param>
/// <param name="Arguments">The arguments of the command line, as given; empty for the window.</param>
public sealed record PlanRecordContext(string Source, string? Profile, IReadOnlyList<string> Arguments);

/// <summary>A plan record as the file holds it.</summary>
/// <param name="Version">The record's format, 1.</param>
/// <param name="Source">The program that applied the plan.</param>
/// <param name="Profile">The tenant profile, or null for the default one.</param>
/// <param name="Arguments">The arguments of the command line.</param>
/// <param name="Plan">The plan, with each change's result once the apply ended.</param>
public sealed record PlanRecord(int Version, string Source, string? Profile, IReadOnlyList<string> Arguments, Plan Plan);

/// <summary>
/// The plan records under the home folder's <c>plans</c> folder (D-005, D-006, task 051): one JSON file per applied
/// plan, <c>yyyyMMddTHHmmss.fffZ-&lt;operation&gt;.json</c> (UTC, so the names sort by time), written when the apply
/// starts (<see cref="PlanMode.Applying"/>, no results) and again when it ends (each change's result, the times). A
/// dry run and a refused plan write nothing. The records stay on the machine, never in the repo.
/// </summary>
public sealed class PlanStore
{
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
    public static PlanStore ForCommandLine(string? profile, IReadOnlyList<string> arguments) =>
        new(AegisHome.Resolve(), new PlanRecordContext("command line", profile, arguments));

    /// <summary>The folder of the records.</summary>
    public string Folder { get; }

    /// <summary>The clock the apply's times come from.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Writes the record of a plan whose apply starts and returns its file; a name taken already gets a <c>-2</c>, <c>-3</c>, ….</summary>
    internal string Begin(Plan plan)
    {
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

    /// <summary>Reads a record back (the tests, and a later plan browser).</summary>
    public static PlanRecord? Read(string path) => JsonSerializer.Deserialize<PlanRecord>(File.ReadAllText(path), RecordOptions);

    private PlanRecord Record(Plan plan) => new(1, _context.Source, _context.Profile, _context.Arguments, plan);
}
