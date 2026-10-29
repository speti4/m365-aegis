using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Audit;

/// <summary>What <c>m365-aegis audit list</c> was asked for.</summary>
/// <param name="Days">The last N days, or null (<see cref="AuditWindow"/>).</param>
/// <param name="Since">The explicit start of the window, or null.</param>
/// <param name="Activity">The start of the activity's name (<c>Add member to group</c>, <c>Update user</c>), or null for every activity.</param>
/// <param name="Limit">The most entries to return; the newest come first.</param>
public sealed record DirectoryAuditQuery(int? Days, DateTimeOffset? Since, string? Activity, int Limit);

/// <summary>The answer of <c>audit list</c>: the window, the filter, and the entries, newest first.</summary>
/// <param name="Since">The start of the window, in UTC.</param>
/// <param name="Activity">The activity filter, or null.</param>
/// <param name="Entries">The audit entries, newest first.</param>
/// <param name="Truncated">More entries matched than the limit let through.</param>
public sealed record DirectoryAuditReport(DateTimeOffset Since, string? Activity, IReadOnlyList<DirectoryAuditEntry> Entries, bool Truncated);

/// <summary>One entry of the Microsoft Entra directory audit log.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="ActivityDateTime">When the activity happened, in UTC.</param>
/// <param name="ActivityDisplayName">The activity (<c>Add member to group</c>).</param>
/// <param name="Category">The category (<c>GroupManagement</c>).</param>
/// <param name="LoggedByService">The service that logged it (<c>Core Directory</c>).</param>
/// <param name="Result"><c>success</c>, <c>failure</c> or <c>timeout</c>.</param>
/// <param name="ResultReason">The reason of a failure or a timeout.</param>
/// <param name="InitiatedBy">Who did it: the user's principal name, or the application's name.</param>
/// <param name="Targets">What it was done to: each target's name, principal name or id.</param>
public sealed record DirectoryAuditEntry(
    string Id,
    DateTimeOffset ActivityDateTime,
    string ActivityDisplayName,
    string? Category,
    string? LoggedByService,
    string? Result,
    string? ResultReason,
    string? InitiatedBy,
    IReadOnlyList<string> Targets);

/// <summary>
/// <c>audit list [--days N | --since &lt;date&gt;] [--activity &lt;name&gt;] [--limit N]</c> over
/// <c>GET /auditLogs/directoryAudits</c> (task 006): <c>activityDateTime ge …</c> and, with <c>--activity</c>,
/// <c>startswith(activityDisplayName, …)</c> in the <c>$filter</c>, newest first (<c>$orderby=activityDateTime
/// desc</c>, which the reference documents, while it states no default order), the pages the client's. Needs
/// <c>AuditLog.Read.All</c> and a role that reads the audit log (Reports Reader, Security Reader); no license beyond
/// Microsoft Entra ID Free.
/// </summary>
public static class DirectoryAuditLookup
{
    /// <summary>The delegated permission the directory audit needs.</summary>
    public const string Permission = "AuditLog.Read.All";

    /// <summary>Runs the lookup.</summary>
    /// <exception cref="AegisException">A usage error, or the failure of a request.</exception>
    public static async Task<CommandResult<DirectoryAuditReport>> RunAsync(GraphClient graph, DirectoryAuditQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(query);
        AuditWindow.CheckLimit(query.Limit);
        DateTimeOffset since = AuditWindow.Start(query.Days, query.Since, now);
        string? activity = query.Activity?.Trim();
        if (activity is { Length: 0 })
        {
            throw new AegisException(FailureKind.Usage, "--activity is empty", "give the start of an activity's name, such as 'Add member to group'");
        }

        var entries = new List<DirectoryAuditEntry>();
        bool truncated = false;
        await foreach (GraphDirectoryAudit item in graph.GetAllAsync<GraphDirectoryAudit>(Request(activity, since), cancellationToken).ConfigureAwait(false))
        {
            if (entries.Count == query.Limit)
            {
                truncated = true;
                break;
            }

            entries.Add(Map(item));
        }

        return CommandResult.Success(new DirectoryAuditReport(since, activity, entries, truncated));
    }

    /// <summary>The request of a window and an activity: <c>/auditLogs/directoryAudits?$filter=…&amp;$orderby=…</c>.</summary>
    internal static GraphRequest Request(string? activity, DateTimeOffset since)
    {
        string filter = $"activityDateTime ge {AuditWindow.Literal(since)}";
        if (activity is not null)
        {
            filter += $" and startswith(activityDisplayName,{GraphQuery.Literal(activity)})";
        }

        string path = GraphQuery.Path("/auditLogs/directoryAudits", ("$filter", filter), ("$orderby", "activityDateTime desc"));
        return GraphRequest.Get(path) with { Permission = Permission };
    }

    private static DirectoryAuditEntry Map(GraphDirectoryAudit item)
    {
        string? initiatedBy = FirstOf(
            item.InitiatedBy?.User?.UserPrincipalName,
            item.InitiatedBy?.User?.DisplayName,
            item.InitiatedBy?.App?.DisplayName,
            item.InitiatedBy?.App?.ServicePrincipalName,
            item.InitiatedBy?.User?.Id,
            item.InitiatedBy?.App?.AppId);
        List<string> targets = (item.TargetResources ?? [])
            .Select(target => FirstOf(target.DisplayName, target.UserPrincipalName, target.Id))
            .OfType<string>()
            .ToList();
        return new DirectoryAuditEntry(
            item.Id ?? "",
            item.ActivityDateTime,
            item.ActivityDisplayName ?? "",
            item.Category,
            item.LoggedByService,
            item.Result,
            string.IsNullOrWhiteSpace(item.ResultReason) ? null : item.ResultReason,
            initiatedBy,
            targets);
    }

    private static string? FirstOf(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>A directory audit entry as Graph sends it (the <c>directoryAudit</c> resource), in the fields the lookup reads.</summary>
internal sealed record GraphDirectoryAudit : GraphObject
{
    public string? Id { get; init; }

    public DateTimeOffset ActivityDateTime { get; init; }

    public string? ActivityDisplayName { get; init; }

    public string? Category { get; init; }

    public string? LoggedByService { get; init; }

    public string? Result { get; init; }

    public string? ResultReason { get; init; }

    public GraphAuditInitiator? InitiatedBy { get; init; }

    public IReadOnlyList<GraphAuditTarget>? TargetResources { get; init; }
}

/// <summary>The <c>auditActivityInitiator</c>: a user or an application.</summary>
internal sealed record GraphAuditInitiator(GraphAuditUser? User, GraphAuditApp? App);

/// <summary>The user who started an activity.</summary>
internal sealed record GraphAuditUser(string? Id, string? DisplayName, string? UserPrincipalName);

/// <summary>The application that started an activity.</summary>
internal sealed record GraphAuditApp(string? AppId, string? DisplayName, string? ServicePrincipalName);

/// <summary>A <c>targetResource</c>, in the fields the lookup reads.</summary>
internal sealed record GraphAuditTarget(string? Id, string? DisplayName, string? UserPrincipalName);
