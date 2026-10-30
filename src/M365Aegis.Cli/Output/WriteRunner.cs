using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The flags of every write command (D-006, task 051): <c>--dry-run</c> prints the plan and exits 0, <c>--yes</c>
/// applies it, and neither prints it and exits 7. Both at once is a usage error. There is no <c>--force</c>. A write
/// of a group or a mailbox also takes <c>--resume &lt;record&gt;</c> (task 062), which <see cref="ResumeAction"/> runs in
/// place of the command's own action.
/// </summary>
internal sealed class WriteFlags
{
    /// <summary>The name of the resume option.</summary>
    internal const string ResumeName = "--resume";

    /// <summary><c>--dry-run</c>.</summary>
    internal Option<bool> DryRun { get; } = new("--dry-run")
    {
        Description = "Print the plan (what would change, from what to what) and send nothing; exit code 0.",
    };

    /// <summary><c>--yes</c>.</summary>
    internal Option<bool> Yes { get; } = new("--yes")
    {
        Description = "Apply the plan and record it under the home folder's plans folder; without it (and without --dry-run) the plan is printed and the exit code is 7.",
    };

    /// <summary>The flags of a write command, added to it; <paramref name="resume"/> adds <c>--resume</c> too.</summary>
    internal static WriteFlags On(Command command, bool resume = true)
    {
        ArgumentNullException.ThrowIfNull(command);
        var flags = new WriteFlags();
        command.Options.Add(flags.DryRun);
        command.Options.Add(flags.Yes);
        if (resume)
        {
            command.Options.Add(new Option<string?>(ResumeName)
            {
                Description = "Continue the plan record of an apply that ended partial or stopped: its failed and skipped changes, planned again against the current state (with --dry-run or --yes, without arguments or --from-csv).",
                Action = new ResumeAction(flags),
            });
        }

        return flags;
    }

    /// <summary>What the flags ask for, or the usage failure of both at once.</summary>
    internal WriteMode? Resolve(ParseResult parsed, out AegisFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        bool dryRun = parsed.GetValue(DryRun);
        bool yes = parsed.GetValue(Yes);
        failure = null;
        if (dryRun && yes)
        {
            failure = new AegisFailure(FailureKind.Usage, "--dry-run and --yes contradict each other", "--dry-run prints the plan, --yes applies it: give one");
            return null;
        }

        return dryRun ? WriteMode.DryRun : yes ? WriteMode.Apply : WriteMode.Refuse;
    }
}

/// <summary>
/// What every write command does around its plan (task 051): the flags read (both at once is a usage error before
/// anything starts), the plan computed by the core over Graph or the Exchange Online layers, and
/// <see cref="PlanGuard"/> printing, refusing or applying it with the plan record in the home folder; the answer is
/// the <see cref="Plan"/>, the table <see cref="PlanTableView"/>. Under <c>--resume</c> (task 062) the plan is narrowed
/// to the changes the record left to do, and the new record keeps the original arguments and file.
/// </summary>
internal static class WriteRunner
{
    /// <summary>The resume the command runs under, set by <see cref="ResumeAction"/> around the command's own action.</summary>
    private static readonly AsyncLocal<ResumeContext?> Resuming = new();

    /// <summary>A write over the Exchange Online layers (and Graph): the plan reads through them, the apply goes through the layer of each change.</summary>
    internal static Task<int> ExchangeAsync(
        ParseResult parsed,
        Option<bool> json,
        Option<bool> table,
        Option<string?> profile,
        WriteFlags flags,
        Func<ExchangeBackends, CancellationToken, Task<WritePlan>> plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(plan);
        if (flags.Resolve(parsed, out AegisFailure? failure) is not { } mode)
        {
            return Task.FromResult(CommandRunner.Fail(Program.ResolveMode(parsed, json, table), failure!));
        }

        PlanStore store = Store(parsed, profile);
        return ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            async (ExchangeBackends backends, CancellationToken ct) => await PlanGuard.RunAsync(Narrow(await plan(backends, ct).ConfigureAwait(false)), mode, store, ct).ConfigureAwait(false),
            PlanTableView.Instance,
            cancellationToken);
    }

    /// <summary>
    /// The plan records of this run: the home folder, the profile, the arguments as given and a <c>--from-csv</c> file's
    /// text; under a resume, the arguments and the file of the record it continues.
    /// </summary>
    internal static PlanStore Store(ParseResult parsed, Option<string?> profile)
    {
        if (Resuming.Value is { } resuming)
        {
            return PlanStore.ForCommandLine(parsed.GetValue(profile), resuming.Arguments, resuming.Input);
        }

        return PlanStore.ForCommandLine(parsed.GetValue(profile), parsed.Tokens.Select(token => token.Value).ToList(), CsvInput(parsed));
    }

    /// <summary>The plan of a resume: the changes the record left to do (<see cref="PlanResume.Narrow"/>); the plan as it is otherwise.</summary>
    private static WritePlan Narrow(WritePlan write) => Resuming.Value is { } resuming ? resuming.Resume.Narrow(write) : write;

    /// <summary>The <c>--from-csv</c> file of a write, kept in its record so a resume replays the same lines (task 062).</summary>
    private static PlanRecordInput? CsvInput(ParseResult parsed)
    {
        OptionResult? csv = parsed.CommandResult.Children.OfType<OptionResult>().FirstOrDefault(option => option.Option.Name == ResumeAction.CsvName && !option.Implicit);
        if (csv?.Tokens.Count is not > 0)
        {
            return null;
        }

        return ReadInput(csv.Tokens[0].Value);
    }

    /// <summary>A CSV file with its full path and text, or null when it cannot be read.</summary>
    internal static PlanRecordInput? ReadInput(string file)
    {
        try
        {
            string path = Path.GetFullPath(file);
            return new PlanRecordInput(path, File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The file was read a moment ago for the plan; a record without its text only loses the resume's replay.
            return null;
        }
    }

    /// <summary>Runs the command's own action under a resume (<see cref="ResumeAction"/>).</summary>
    internal static async Task<int> RunResumingAsync(ResumeContext context, Func<Task<int>> run)
    {
        ResumeContext? before = Resuming.Value;
        Resuming.Value = context;
        try
        {
            return await run().ConfigureAwait(false);
        }
        finally
        {
            Resuming.Value = before;
        }
    }

    /// <summary>
    /// Reads the rows of <c>--from-csv</c> before anything starts, so a malformed file is refused as a whole with no
    /// sign-in and no request (task 054); the usage failure of a bad file, or of the file next to arguments, renders
    /// at once. Under a resume the rows are narrowed to the ones its record left to do (task 065,
    /// <see cref="PlanResume.Select{T}"/>), so the plan reads only the objects they name.
    /// </summary>
    internal static bool TryRows<T>(ParseResult parsed, Option<bool> json, Option<bool> table, Func<IReadOnlyList<T>> read, out IReadOnlyList<T> rows, out int exitCode)
        where T : IPlanRow
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            rows = read();
            if (Resuming.Value is { } resuming)
            {
                rows = resuming.Resume.Select(rows);
            }

            exitCode = 0;
            return true;
        }
        catch (AegisException exception)
        {
            rows = [];
            exitCode = CommandRunner.Fail(Program.ResolveMode(parsed, json, table), exception.Failure);
            return false;
        }
    }
}

/// <summary>A resume as the command's own action sees it: the record, and the arguments and file its new record keeps.</summary>
/// <param name="Resume">The record and the changes it left to do.</param>
/// <param name="Arguments">The original command's arguments with this run's flags, for the new record.</param>
/// <param name="Input">The original CSV file's text, for the new record; null for a plan from the arguments.</param>
internal sealed record ResumeContext(PlanResume Resume, IReadOnlyList<string> Arguments, PlanRecordInput? Input);

/// <summary>
/// <c>--resume &lt;record&gt;</c> on a write command (task 062), run in place of the command's own action: the record is
/// read (a missing one is exit code 4; a raw call, a record of another write, of another profile, or with every change
/// applied is a usage error), the original command line is parsed again from the record with this run's flags (a CSV
/// file from the text the record keeps), and the command's own action runs under the resume, so its plan is narrowed
/// to the changes the record left to do and applied under the usual guard. Arguments or <c>--from-csv</c> beside it are
/// a usage error: the record names the changes.
/// </summary>
internal sealed class ResumeAction(WriteFlags flags) : AsynchronousCommandLineAction
{
    /// <summary>The CSV option of the writes (task 054).</summary>
    internal const string CsvName = "--from-csv";

    /// <summary>The options a resume may be given beside it: the flags and the root's options.</summary>
    private static readonly string[] Allowed = [WriteFlags.ResumeName, "--dry-run", "--yes", "--json", "--table", "--profile", "--verbose"];

    /// <summary>The flags whose value this run gives, never the record's.</summary>
    private static readonly string[] RunFlags = ["--dry-run", "--yes", "--json", "--table", "--verbose"];

    public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        OutputMode mode = Mode(parseResult);
        string? temporary = null;
        try
        {
            if (flags.Resolve(parseResult, out AegisFailure? both) is null)
            {
                return CommandRunner.Fail(mode, both!);
            }

            if (parseResult.CommandResult.Children.Any(child => child switch
            {
                ArgumentResult argument => argument.Tokens.Count > 0,
                OptionResult option => !option.Implicit && !Allowed.Contains(option.Option.Name, StringComparer.Ordinal),
                _ => false,
            }))
            {
                return CommandRunner.Fail(mode, new AegisFailure(
                    FailureKind.Usage,
                    "give --resume alone, without the arguments or --from-csv",
                    "the record names the changes to continue; nothing was sent"));
            }

            PlanResume resume = PlanResume.Open(AegisHome.Resolve(), parseResult.GetValue<string?>(WriteFlags.ResumeName)!);
            resume.EnsureOperation(Operation(parseResult.CommandResult.Command));
            string? profile = parseResult.GetValue<string?>("--profile");
            if (!string.Equals(resume.Original.Profile, profile, StringComparison.Ordinal))
            {
                return CommandRunner.Fail(mode, new AegisFailure(
                    FailureKind.Usage,
                    $"'{Path.GetFileName(resume.Record)}' was applied with the profile {resume.Original.Profile ?? "(the default one)"}, not {profile ?? "(the default one)"}",
                    resume.Original.Profile is null ? "run the resume without --profile" : $"run the resume with --profile {resume.Original.Profile}"));
            }

            List<string> original = resume.Original.Arguments.Where(token => !RunFlags.Contains(token, StringComparer.Ordinal)).ToList();
            List<string> mine = parseResult.Tokens.Select(token => token.Value).Where(token => RunFlags.Contains(token, StringComparer.Ordinal)).ToList();
            List<string> arguments = [.. original, .. mine];
            List<string> tokens = arguments;
            if (resume.Original.Input is { } input && Csv(arguments) is { } at)
            {
                temporary = Path.Combine(Path.GetTempPath(), "m365-aegis-resume", Guid.NewGuid().ToString("N"), Path.GetFileName(input.File));
                Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
                await File.WriteAllTextAsync(temporary, input.Content, cancellationToken).ConfigureAwait(false);
                tokens = [.. arguments];
                tokens[at] = temporary;
            }

            ParseResult again = parseResult.RootCommandResult.Command.Parse(tokens);
            if (again.Errors.Count > 0 || !ReferenceEquals(again.CommandResult.Command, parseResult.CommandResult.Command))
            {
                return CommandRunner.Fail(mode, new AegisFailure(
                    FailureKind.Usage,
                    $"the arguments of '{Path.GetFileName(resume.Record)}' do not make the same command again: {string.Join(" ", again.Errors.Select(error => error.Message))}",
                    "run the original command again instead; its plan leaves what is applied unchanged"));
            }

            return await WriteRunner.RunResumingAsync(
                new ResumeContext(resume, arguments, resume.Original.Input),
                () => again.InvokeAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (AegisException exception)
        {
            return CommandRunner.Fail(mode, exception.Failure);
        }
        finally
        {
            if (temporary is not null)
            {
                DeleteQuietly(Path.GetDirectoryName(temporary)!);
            }
        }
    }

    /// <summary>Removes a temporary folder; one that cannot be removed is the system's to clean.</summary>
    internal static void DeleteQuietly(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A temporary copy left behind is the system's to clean.
        }
    }

    /// <summary>A write's stable name from its command's path: <c>group members add</c> is <c>group-members-add</c>.</summary>
    private static string Operation(Command command)
    {
        var names = new List<string>();
        // Every command of the tree has the root above it, so the walk ends there.
        for (Command current = command; current is not RootCommand; current = current.Parents.OfType<Command>().First())
        {
            names.Insert(0, current.Name);
        }

        return string.Join("-", names);
    }

    /// <summary>The position of the <c>--from-csv</c> file among the tokens, or null.</summary>
    private static int? Csv(List<string> tokens)
    {
        int option = tokens.FindIndex(token => token == CsvName);
        return option >= 0 && option + 1 < tokens.Count ? option + 1 : null;
    }

    /// <summary>The output mode from the root's options, by name: the action has no handle on them.</summary>
    private static OutputMode Mode(ParseResult parsed) =>
        Program.PickMode(parsed.GetValue<bool>("--json"), parsed.GetValue<bool>("--table"), Console.IsOutputRedirected);
}
