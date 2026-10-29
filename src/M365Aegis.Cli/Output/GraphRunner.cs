using System.CommandLine;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// What every Graph command does around its work: the output mode from the root options, a
/// <see cref="GraphClient"/> for the profile (the token is asked for silently on the first request; the fixtures
/// when <see cref="GraphConnection.FixturesEnvironmentVariable"/> is set), the request ids into the envelope's
/// meta, and <see cref="Backend.Graph"/> as the backend. A lookup command's action is one call:
/// <c>command.SetAction((parsed, cancellationToken) =&gt; GraphRunner.RunAsync(parsed, json, table, profile,
/// (graph, ct) =&gt; Lookup(graph, …, ct), View.Instance, cancellationToken))</c>.
/// </summary>
internal static class GraphRunner
{
    /// <summary>Runs a Graph command's work and renders its result; the return value is the exit code.</summary>
    internal static Task<int> RunAsync<T>(
        ParseResult parsed,
        Option<bool> json,
        Option<bool> table,
        Option<string?> profile,
        Func<GraphClient, CancellationToken, Task<CommandResult<T>>> work,
        ITableView<T> view,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(work);
        return CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Graph,
            requestLog => work(GraphConnection.Create(parsed.GetValue(profile), requestLog), cancellationToken),
            view);
    }
}
