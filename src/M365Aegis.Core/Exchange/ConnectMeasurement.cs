using System.Diagnostics;
using System.Text.RegularExpressions;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>The three connects task 011 compares; the sidecar uses <see cref="CommandName"/>.</summary>
public enum ConnectVariant
{
    /// <summary><c>Connect-ExchangeOnline -AccessToken … -Organization …</c> with nothing else.</summary>
    Default,

    /// <summary>The same with <c>-SkipLoadingFormatData</c>.</summary>
    SkipFormatData,

    /// <summary>The same with <c>-SkipLoadingFormatData -CommandName</c> and the REST cmdlets of the allow-list.</summary>
    CommandName,
}

/// <summary>A median and the spread of a measure over a variant's runs, in milliseconds; null when no run had it.</summary>
/// <param name="Median">The median.</param>
/// <param name="Min">The fastest run.</param>
/// <param name="Max">The slowest run.</param>
public sealed record Spread(long? Median, long? Min, long? Max);

/// <summary>One fresh sidecar of the measurement.</summary>
/// <param name="Variant">The connect's variant.</param>
/// <param name="Run">The run's number within the variant, from 1.</param>
/// <param name="TotalMs">From starting pwsh to the end of the first cmdlet.</param>
/// <param name="StartMs">From starting pwsh to its <c>hello</c>.</param>
/// <param name="ImportMs">Finding the module on the module path and importing it.</param>
/// <param name="ConnectMs">The <c>Connect-ExchangeOnline -AccessToken</c> call.</param>
/// <param name="FirstCallMs">A first REST cmdlet of the allow-list (<c>Get-AcceptedDomain</c>), end to end.</param>
/// <param name="Missing">The allowed cmdlets the session did not answer.</param>
/// <param name="Error">Why the run failed, or null.</param>
public sealed record ConnectRun(ConnectVariant Variant, int Run, long TotalMs, long StartMs, long ImportMs, long ConnectMs, long FirstCallMs, IReadOnlyList<string> Missing, string? Error);

/// <summary>A variant's runs in figures.</summary>
/// <param name="Variant">The variant.</param>
/// <param name="Runs">How many runs.</param>
/// <param name="Failed">How many of them failed (left out of the figures).</param>
/// <param name="Total">From starting pwsh to the end of the first cmdlet.</param>
/// <param name="Start">From starting pwsh to its <c>hello</c>.</param>
/// <param name="Import">The module's discovery and import.</param>
/// <param name="Connect">The connect.</param>
/// <param name="FirstCall">The first cmdlet.</param>
/// <param name="AnswersAllowList">Whether every run that connected answered every cmdlet of the allow-list.</param>
public sealed record VariantSummary(ConnectVariant Variant, int Runs, int Failed, Spread Total, Spread Start, Spread Import, Spread Connect, Spread FirstCall, bool AnswersAllowList);

/// <summary>Where the measurement ran: versions and the OneDrive checks, never a path or an account.</summary>
/// <param name="Pwsh">The pwsh version.</param>
/// <param name="Module">The ExchangeOnlineManagement version.</param>
/// <param name="ModuleUnderOneDrive">Whether the module's folder is under a OneDrive-synced folder.</param>
/// <param name="FirstModulePathUnderOneDrive">Whether the first entry of the child's <c>PSModulePath</c> is.</param>
/// <param name="ModulePathEntriesUnderOneDrive">How many entries of the child's <c>PSModulePath</c> are.</param>
/// <param name="DocumentsUnderOneDrive">Whether the Documents folder is (OneDrive's Known Folder Move).</param>
/// <param name="AllowList">The allow-list the <c>command-name</c> variant imports from.</param>
/// <param name="TokenPermissions">The permissions of the one token every run connected with.</param>
public sealed record MeasureEnvironment(
    string? Pwsh,
    string? Module,
    bool? ModuleUnderOneDrive,
    bool? FirstModulePathUnderOneDrive,
    int ModulePathEntriesUnderOneDrive,
    bool DocumentsUnderOneDrive,
    IReadOnlyList<string> AllowList,
    IReadOnlyList<string> TokenPermissions);

/// <summary>The result of <c>probe exchange-measure</c> (task 011).</summary>
/// <param name="Environment">Where it ran.</param>
/// <param name="Summary">A row per variant, in the order asked.</param>
/// <param name="Runs">Every run.</param>
public sealed record ConnectMeasurementReport(MeasureEnvironment Environment, IReadOnlyList<VariantSummary> Summary, IReadOnlyList<ConnectRun> Runs);

/// <summary>
/// The cold connect of the sidecar, measured (task 011): the broker's token is asked for once, silently, and every
/// run starts a fresh pwsh with the sidecar script and connects it with that token the way the sidecar does
/// (<c>-AccessToken</c> over the protocol, never on a command line), in the variant asked, then runs one REST cmdlet
/// and ends the process. No interactive sign-in, no token or path in the report.
/// </summary>
public static partial class ConnectMeasurement
{
    /// <summary>The variables Windows sets to the OneDrive folders it syncs.</summary>
    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveCommercial", "OneDriveConsumer"];

    /// <summary>The variants by their names on the command line.</summary>
    public static ConnectVariant ParseVariant(string name) => name.Trim().ToUpperInvariant() switch
    {
        "DEFAULT" => ConnectVariant.Default,
        "SKIP-FORMAT-DATA" => ConnectVariant.SkipFormatData,
        "COMMAND-NAME" => ConnectVariant.CommandName,
        _ => throw new AegisException(FailureKind.Usage, $"'{name}' is not a connect variant", "use default, skip-format-data or command-name"),
    };

    /// <summary>A variant's name on the command line and in the tables.</summary>
    public static string Name(ConnectVariant variant) => variant switch
    {
        ConnectVariant.SkipFormatData => "skip-format-data",
        ConnectVariant.CommandName => "command-name",
        _ => "default",
    };

    /// <summary>Runs every variant <paramref name="runs"/> times, each in a fresh sidecar, with one token.</summary>
    public static async Task<ConnectMeasurementReport> RunAsync(
        ExchangeSessionOptions session, IReadOnlyList<ConnectVariant> variants, int runs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(variants);
        if (runs is < 1 or > 20)
        {
            throw new AegisException(FailureKind.Usage, $"--runs must be 1 to 20, not {runs}", "five runs per variant are the measurement of task 011");
        }

        ExoCmdlets.EnsureAllowed(ExoCmdlets.GetAcceptedDomain);
        ExchangeTarget target = await session.Target(cancellationToken).ConfigureAwait(false);
        ExchangeToken token = await session.Token(cancellationToken).ConfigureAwait(false);
        var results = new List<ConnectRun>();
        SidecarHello? hello = null;
        foreach (ConnectVariant variant in variants)
        {
            for (int run = 1; run <= runs; run++)
            {
                (ConnectRun result, SidecarHello? seen) = await RunOnceAsync(session, token, target, variant, run, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                hello ??= seen;
            }
        }

        return new ConnectMeasurementReport(
            Describe(hello, token),
            variants.Select(variant => Summarize(variant, results.Where(result => result.Variant == variant).ToList())).ToList(),
            results);
    }

    /// <summary>Whether a path lies under a OneDrive-synced folder: one of the roots, or a <c>OneDrive</c> folder in it.</summary>
    public static bool UnderOneDrive(string? path, IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return roots.Any(root => !string.IsNullOrWhiteSpace(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            || OneDriveFolder().IsMatch(path);
    }

    /// <summary>The median and the spread of the values; nulls when there are none.</summary>
    public static Spread SpreadOf(IReadOnlyList<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return new Spread(null, null, null);
        }

        List<long> sorted = values.Order().ToList();
        int middle = sorted.Count / 2;
        long median = sorted.Count % 2 == 1 ? sorted[middle] : (long)Math.Round((sorted[middle - 1] + sorted[middle]) / 2.0, MidpointRounding.AwayFromZero);
        return new Spread(median, sorted[0], sorted[^1]);
    }

    private static async Task<(ConnectRun Run, SidecarHello? Hello)> RunOnceAsync(
        ExchangeSessionOptions session, ExchangeToken token, ExchangeTarget target, ConnectVariant variant, int run, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        SidecarHello? hello = null;
        try
        {
            await using SidecarClient client = await session.Start(cancellationToken).ConfigureAwait(false);
            hello = client.Hello;
            var connect = new SidecarConnect(token.AccessToken, target.Organization, target.UserPrincipalName)
            {
                SkipLoadingFormatData = variant != ConnectVariant.Default,
                CommandName = variant == ConnectVariant.CommandName,
                CheckCommands = true,
            };
            SidecarConnected connected = await client.ConnectAsync(connect, again: false, cancellationToken).ConfigureAwait(false);
            var first = Stopwatch.StartNew();
            await client.InvokeAsync(new ExoCommand(ExoCmdlets.GetAcceptedDomain) { Properties = ["DomainName"] }, cancellationToken).ConfigureAwait(false);
            long firstMs = first.ElapsedMilliseconds;
            long totalMs = total.ElapsedMilliseconds;
            return (new ConnectRun(variant, run, totalMs, hello.StartMs, connected.ImportMs, connected.ConnectMs, firstMs, connected.Missing ?? [], null), hello);
        }
        catch (AegisException exception)
        {
            return (new ConnectRun(variant, run, total.ElapsedMilliseconds, hello?.StartMs ?? 0, 0, 0, 0, [], $"{exception.Failure.Code}: {exception.Failure.Message}"), hello);
        }
    }

    private static VariantSummary Summarize(ConnectVariant variant, List<ConnectRun> runs)
    {
        List<ConnectRun> ok = runs.Where(run => run.Error is null).ToList();
        return new VariantSummary(
            variant,
            runs.Count,
            runs.Count - ok.Count,
            SpreadOf(ok.Select(run => run.TotalMs).ToList()),
            SpreadOf(ok.Select(run => run.StartMs).ToList()),
            SpreadOf(ok.Select(run => run.ImportMs).ToList()),
            SpreadOf(ok.Select(run => run.ConnectMs).ToList()),
            SpreadOf(ok.Select(run => run.FirstCallMs).ToList()),
            ok.Count > 0 && ok.All(run => run.Missing.Count == 0));
    }

    private static MeasureEnvironment Describe(SidecarHello? hello, ExchangeToken token)
    {
        string[] roots = [.. OneDriveVariables
            .Select(Environment.GetEnvironmentVariable)
            .OfType<string>()
            .Where(root => root.Length > 0)];
        string[] entries = hello?.ModulePath?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return new MeasureEnvironment(
            hello?.Pwsh,
            hello?.Module,
            hello?.ModuleBase is null ? null : UnderOneDrive(hello.ModuleBase, roots),
            entries.Length == 0 ? null : UnderOneDrive(entries[0], roots),
            entries.Count(entry => UnderOneDrive(entry, roots)),
            UnderOneDrive(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), roots),
            ExoCmdlets.Allowed,
            token.Permissions);
    }

    [GeneratedRegex(@"[\\/]OneDrive( - [^\\/]+)?([\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OneDriveFolder();
}
