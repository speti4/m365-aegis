using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Audit;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis signin list [--user &lt;upn&gt;] [--failures] [--days N | --since &lt;date&gt;] [--limit N]</c>: the
/// Microsoft Entra sign-in log (task 006). The result type is <see cref="SignInReport"/>, the table
/// <see cref="SignInListTableView"/>.
/// </summary>
internal static class SignInListCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<string?> user = new("--user")
        {
            Description = "The user principal name whose sign-ins to list (every user when omitted).",
        };
        Option<bool> failures = new("--failures")
        {
            Description = "Only the failed sign-ins.",
        };
        Option<int?> days = AuditOptions.Days();
        Option<string?> since = AuditOptions.Since();
        Option<int> limit = AuditOptions.Limit("sign-ins");
        Command list = new("list", "List the sign-ins of the Microsoft Entra sign-in log, newest first (needs Microsoft Entra ID P1 or P2).");
        list.Options.Add(user);
        list.Options.Add(failures);
        list.Options.Add(days);
        list.Options.Add(since);
        list.Options.Add(limit);
        list.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed,
            json,
            table,
            profile,
            (graph, ct) => SignInLookup.RunAsync(
                graph,
                new SignInQuery(
                    parsed.GetValue(user),
                    parsed.GetValue(failures),
                    parsed.GetValue(days),
                    AuditWindow.ParseSince(parsed.GetValue(since)),
                    parsed.GetValue(limit)),
                DateTimeOffset.UtcNow,
                ct),
            SignInListTableView.Instance,
            cancellationToken));

        Command command = new("signin", "The Microsoft Entra sign-in log.");
        command.Subcommands.Add(list);
        return command;
    }
}

/// <summary>The table of <c>signin list</c>: a sign-in per row, newest first, the window and the count under it.</summary>
internal sealed class SignInListTableView : ITableView<SignInReport>
{
    /// <summary>The one instance.</summary>
    internal static SignInListTableView Instance { get; } = new();

    private SignInListTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["time (UTC)", "user", "app", "IP address", "location", "result"];

    public IEnumerable<IReadOnlyList<string>> Rows(SignInReport data) => data.SignIns.Select(entry => (IReadOnlyList<string>)
    [
        AuditOptions.Time(entry.CreatedDateTime),
        AuditOptions.Cut(entry.UserPrincipalName, 40),
        AuditOptions.Cut(entry.AppDisplayName, 30),
        AuditOptions.Cut(entry.IpAddress, 39),
        AuditOptions.Cut(entry.Location, 24),
        entry.Succeeded
            ? "success"
            : AuditOptions.Cut($"{entry.ErrorCode.ToString(CultureInfo.InvariantCulture)} {entry.FailureReason}", 60),
    ]);

    public IEnumerable<string> Footer(SignInReport data)
    {
        string what = data.FailuresOnly ? "failed sign-in(s)" : "sign-in(s)";
        string who = data.User is null ? "" : $" of {data.User}";
        yield return string.Format(CultureInfo.InvariantCulture, "{0} {1}{2} since {3} UTC", data.SignIns.Count, what, who, AuditOptions.Time(data.Since));
        if (data.Truncated)
        {
            yield return "more sign-ins match: --limit raises the cap, --days or --since narrows the window";
        }
    }
}
