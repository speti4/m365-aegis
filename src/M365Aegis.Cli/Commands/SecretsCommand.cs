using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Audit;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis secrets expiring &lt;days&gt; [--include-expired]</c>: the client secrets and certificates of the
/// application registrations and the service principals that expire within the window (task 006). The result
/// type is <see cref="CredentialExpiryReport"/>, the table <see cref="SecretsExpiringTableView"/>.
/// </summary>
internal static class SecretsCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Argument<int> days = new("days")
        {
            Description = "List the credentials that expire within this many days.",
        };
        Option<bool> includeExpired = new("--include-expired")
        {
            Description = "Also list the credentials that have already expired and are still on the application.",
        };
        Command expiring = new("expiring", "List the application secrets and certificates that expire within the given days, the soonest first.");
        expiring.Arguments.Add(days);
        expiring.Options.Add(includeExpired);
        expiring.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed,
            json,
            table,
            profile,
            (graph, ct) => CredentialExpiryLookup.RunAsync(
                graph,
                new CredentialExpiryQuery(parsed.GetValue(days), parsed.GetValue(includeExpired)),
                DateTimeOffset.UtcNow,
                ct),
            SecretsExpiringTableView.Instance,
            cancellationToken));

        Command command = new("secrets", "The secrets and certificates of the application registrations and service principals.");
        command.Subcommands.Add(expiring);
        return command;
    }
}

/// <summary>The table of <c>secrets expiring</c>: a credential per row, the soonest to expire first.</summary>
internal sealed class SecretsExpiringTableView : ITableView<CredentialExpiryReport>
{
    /// <summary>The one instance.</summary>
    internal static SecretsExpiringTableView Instance { get; } = new();

    private SecretsExpiringTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["expires (UTC)", "days left", "application", "on", "kind", "credential"];

    public IEnumerable<IReadOnlyList<string>> Rows(CredentialExpiryReport data) => data.Credentials.Select(credential => (IReadOnlyList<string>)
    [
        AuditOptions.Time(credential.EndDateTime),
        credential.Expired ? "expired" : credential.DaysLeft.ToString(CultureInfo.InvariantCulture),
        AuditOptions.Cut(credential.DisplayName, 40),
        credential.Owner == CredentialOwner.Application ? "app registration" : "service principal",
        credential.Kind == CredentialKind.Secret ? "secret" : "certificate",
        AuditOptions.Cut(credential.Name ?? credential.KeyId, 36),
    ]);

    public IEnumerable<string> Footer(CredentialExpiryReport data)
    {
        int expired = data.Credentials.Count(credential => credential.Expired);
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} credential(s) expire within {1} day(s){2}; {3} app registration(s) and {4} service principal(s) checked",
            data.Credentials.Count - expired,
            data.Days,
            data.IncludeExpired ? string.Format(CultureInfo.InvariantCulture, ", {0} already expired", expired) : "",
            data.Applications,
            data.ServicePrincipals);
    }
}
