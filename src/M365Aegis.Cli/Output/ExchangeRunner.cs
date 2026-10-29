using System.CommandLine;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
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

    /// <summary>
    /// Runs a mailbox command that reads through several layers (task 042): its <see cref="ExchangeBackends"/> are the
    /// sidecar, the Admin API (null in the sidecar-only offline tests), Graph and more sidecars on demand, all over one
    /// sign-in of the profile (the fixtures when <c>M365AEGIS_GRAPH_FIXTURES</c> is set); the envelope's backend names
    /// the layers the command used (<c>admin-api+pwsh</c>).
    /// </summary>
    internal static Task<int> RunAsync<T>(
        ParseResult parsed,
        Option<bool> json,
        Option<bool> table,
        Option<string?> profile,
        Func<ExchangeBackends, CancellationToken, Task<CommandResult<T>>> work,
        ITableView<T> view,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(work);
        ExchangeBackends? used = null;
        return CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            () => used?.Backend ?? Backend.Pwsh,
            async requestLog =>
            {
                string? name = parsed.GetValue(profile);
                var auth = new Lazy<AuthService>(AuthService.Create);
                await using ExchangeSession session = ExchangeConnection.Create(name, requestLog, () => auth.Value);
                await using var backends = new ExchangeBackends(
                    session,
                    AdminApiConnection.Create(name, requestLog, () => auth.Value),
                    () => GraphConnection.Create(name, requestLog, () => auth.Value),
                    () => ExchangeConnection.Create(name, requestLog, () => auth.Value));
                used = backends;
                return await work(backends, cancellationToken).ConfigureAwait(false);
            },
            view);
    }
}
