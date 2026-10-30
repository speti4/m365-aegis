using System.CommandLine;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The two flags of every write command (D-006, task 051): <c>--dry-run</c> prints the plan and exits 0, <c>--yes</c>
/// applies it, and neither prints it and exits 7. Both at once is a usage error. There is no <c>--force</c>.
/// </summary>
internal sealed class WriteFlags
{
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

    /// <summary>The flags of a write command, added to it.</summary>
    internal static WriteFlags On(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var flags = new WriteFlags();
        command.Options.Add(flags.DryRun);
        command.Options.Add(flags.Yes);
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
/// the <see cref="Plan"/>, the table <see cref="PlanTableView"/>.
/// </summary>
internal static class WriteRunner
{
    /// <summary>A write over Graph: <paramref name="plan"/> reads the current state and returns the plan.</summary>
    internal static Task<int> GraphAsync(
        ParseResult parsed,
        Option<bool> json,
        Option<bool> table,
        Option<string?> profile,
        WriteFlags flags,
        Func<GraphClient, CancellationToken, Task<WritePlan>> plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(plan);
        if (flags.Resolve(parsed, out AegisFailure? failure) is not { } mode)
        {
            return Task.FromResult(CommandRunner.Fail(Program.ResolveMode(parsed, json, table), failure!));
        }

        PlanStore store = Store(parsed, profile);
        return GraphRunner.RunAsync(
            parsed, json, table, profile,
            async (graph, ct) => await PlanGuard.RunAsync(await plan(graph, ct).ConfigureAwait(false), mode, store, ct).ConfigureAwait(false),
            PlanTableView.Instance,
            cancellationToken);
    }

    /// <summary>A write over the Exchange Online layers: the plan reads through them, the apply goes through the sidecar.</summary>
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
            async (ExchangeBackends backends, CancellationToken ct) => await PlanGuard.RunAsync(await plan(backends, ct).ConfigureAwait(false), mode, store, ct).ConfigureAwait(false),
            PlanTableView.Instance,
            cancellationToken);
    }

    /// <summary>The plan records of this run: the home folder, the profile and the arguments as given.</summary>
    internal static PlanStore Store(ParseResult parsed, Option<string?> profile) =>
        PlanStore.ForCommandLine(parsed.GetValue(profile), parsed.Tokens.Select(token => token.Value).ToList());

    /// <summary>
    /// Reads the rows of <c>--from-csv</c> before anything starts, so a malformed file is refused as a whole with no
    /// sign-in and no request (task 054); the usage failure of a bad file, or of the file next to arguments, renders
    /// at once.
    /// </summary>
    internal static bool TryRows<T>(ParseResult parsed, Option<bool> json, Option<bool> table, Func<IReadOnlyList<T>> read, out IReadOnlyList<T> rows, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            rows = read();
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
