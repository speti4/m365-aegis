using System.CommandLine;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// What every Exchange Online command does around its work, the same shape as <see cref="GraphRunner"/>: the output
/// mode from the root options, an <see cref="ExchangeSession"/> for the profile (one sidecar for the run, started
/// on the first cmdlet and ended with the command; the offline tests' organization when
/// <see cref="ExchangeConnection.TestOrganizationVariable"/> is set), and <see cref="Backend.Pwsh"/> as the backend.
/// A mailbox command's action is one call: <c>ExchangeRunner.RunAsync(parsed, json, table, profile,
/// (exo, ct) =&gt; MailboxLookups.ListAsync(exo, …, ct), View.Instance, cancellationToken)</c>.
/// </summary>
internal static class ExchangeRunner
{
    /// <summary>Runs an Exchange Online command's work and renders its result; the return value is the exit code.</summary>
    internal static Task<int> RunAsync<T>(
        ParseResult parsed,
        Option<bool> json,
        Option<bool> table,
        Option<string?> profile,
        Func<IExoCmdletRunner, CancellationToken, Task<CommandResult<T>>> work,
        ITableView<T> view,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(work);
        return CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Pwsh,
            async requestLog =>
            {
                await using ExchangeSession session = ExchangeConnection.Create(parsed.GetValue(profile), requestLog);
                return await work(session, cancellationToken).ConfigureAwait(false);
            },
            view);
    }
}
