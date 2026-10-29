using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Audit;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis audit list [--days N | --since &lt;date&gt;] [--until &lt;date&gt;] [--activity &lt;name&gt;]
/// [--user &lt;upn&gt;] [--limit N]</c>: the Microsoft Entra directory audit log (task 006; <c>--user</c> task 037,
/// <c>--until</c> and the truncated footer task 038). The result type is <see cref="DirectoryAuditReport"/>, the table
/// <see cref="AuditListTableView"/>.
/// </summary>
internal static class AuditCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<int?> days = AuditOptions.Days();
        Option<string?> since = AuditOptions.Since();
        Option<string?> activity = new("--activity")
        {
            Description = "Only the activities whose name starts with this text ('Add member to group', 'Update user').",
        };
        Option<string?> user = new("--user")
        {
            Description = "Only the activities this user started, by user principal name (alice@contoso.com).",
        };
        Option<string?> until = AuditOptions.Until();
        Option<int> limit = AuditOptions.Limit("entries");
        Command list = new("list", "List the entries of the Microsoft Entra directory audit log, newest first.");
        list.Options.Add(days);
        list.Options.Add(since);
        list.Options.Add(until);
        list.Options.Add(activity);
        list.Options.Add(user);
        list.Options.Add(limit);
        list.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed,
            json,
            table,
            profile,
            (graph, ct) => DirectoryAuditLookup.RunAsync(
                graph,
                new DirectoryAuditQuery(
                    parsed.GetValue(days),
                    AuditWindow.ParseSince(parsed.GetValue(since)),
                    parsed.GetValue(activity),
                    parsed.GetValue(limit),
                    parsed.GetValue(user),
                    AuditWindow.ParseUntil(parsed.GetValue(until))),
                DateTimeOffset.UtcNow,
                ct),
            AuditListTableView.Instance,
            cancellationToken));

        Command command = new("audit", "The Microsoft Entra directory audit log.");
        command.Subcommands.Add(list);
        return command;
    }
}

/// <summary>The table of <c>audit list</c>: an entry per row, newest first, the window and the count under it.</summary>
internal sealed class AuditListTableView : ITableView<DirectoryAuditReport>
{
    /// <summary>The one instance.</summary>
    internal static AuditListTableView Instance { get; } = new();

    private AuditListTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["time (UTC)", "activity", "initiated by", "target", "result"];

    public IEnumerable<IReadOnlyList<string>> Rows(DirectoryAuditReport data) => data.Entries.Select(entry => (IReadOnlyList<string>)
    [
        AuditOptions.Time(entry.ActivityDateTime),
        AuditOptions.Cut(entry.ActivityDisplayName, 40),
        AuditOptions.Cut(entry.InitiatedBy, 40),
        AuditOptions.Cut(string.Join(", ", entry.Targets), 50),
        entry.ResultReason is null || entry.Result == "success"
            ? AuditOptions.Cut(entry.Result, 20)
            : AuditOptions.Cut($"{entry.Result}: {entry.ResultReason}", 50),
    ]);

    public IEnumerable<string> Footer(DirectoryAuditReport data)
    {
        string filter = data.Activity is null ? "" : $" starting with '{data.Activity}'";
        string who = data.User is null ? "" : $" initiated by {data.User}";
        yield return string.Format(CultureInfo.InvariantCulture, "{0} entry(ies){1}{2} {3}", data.Entries.Count, filter, who, AuditOptions.Window(data.Since, data.Until));
        if (data.Truncated)
        {
            yield return AuditOptions.Truncated("entry", data.Entries.Count, data.Since, data.Oldest);
        }
    }
}
