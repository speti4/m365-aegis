using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Groups;

/// <summary>Which list of a group a write changes.</summary>
public enum GroupRelation
{
    /// <summary>The direct members.</summary>
    Members,

    /// <summary>The owners.</summary>
    Owners,
}

/// <summary>One change a group write was asked for: a user added to or removed from a group's list.</summary>
/// <param name="Group">The group as given: a display name, an address, an object id or a word of its name.</param>
/// <param name="User">The user as given: a user principal name, or the object id of any directory object.</param>
/// <param name="Row">The CSV line it comes from (task 054), or null for the arguments.</param>
public sealed record GroupChangeRow(string Group, string User, int? Row = null);

/// <summary>
/// The writes of the groups domain (task 052): <c>group members add|remove</c> and <c>group owners add|remove</c>, each
/// a <see cref="WritePlan"/>. The plan reads the group's current list and lists only what is not so already: a member
/// already there, or one to remove who is not, is left alone. A user to add is resolved first (a user principal name
/// through <c>/users</c>, an object id through <c>/directoryObjects</c>, twenty to a <c>$batch</c> call), and one that
/// does not exist fails the whole plan before anything is sent. A change to a Microsoft 365 or security group is one
/// Graph <c>$ref</c> request (<c>POST /groups/{id}/members/$ref</c> with the object's <c>@odata.id</c>,
/// <c>DELETE /groups/{id}/members/{object id}/$ref</c>, the owners alike), sent twenty to a <c>$batch</c> call with each
/// change's result read from its own part (task 058). The members of a distribution or mail-enabled security group,
/// which Exchange manages and Graph refuses to change, go through Exchange when the run has its layers (task 061): the
/// current members from the Admin API's <c>Get-DistributionGroupMember</c> (Graph's list when the run has no Admin API
/// or it cannot answer), and each change through the sidecar's <c>Add-DistributionGroupMember</c> or
/// <c>Remove-DistributionGroupMember</c>. The plan warns when a group would lose its last owner, and when the service
/// will refuse the change (a group synced from on-premises Active Directory, a group Graph keeps no owners for).
/// </summary>
public static class GroupWrites
{
    /// <summary>The permission a member write needs (Add members, Remove member).</summary>
    public const string MemberWritePermission = "GroupMember.ReadWrite.All";

    /// <summary>The permission an owner write needs (Add owners, Remove owner).</summary>
    public const string OwnerWritePermission = "Group.ReadWrite.All";

    /// <summary>The permission the resolution of a user to add needs.</summary>
    public const string UserReadPermission = "User.ReadBasic.All";

    /// <summary>The columns of a <c>--from-csv</c> file of a group write.</summary>
    public static IReadOnlyList<string> CsvColumns { get; } = ["group", "user"];

    private const string UserFields = "id,displayName,userPrincipalName,mail";

    /// <summary>The names Exchange may give a distribution group's member, each matched against a user as given.</summary>
    private static readonly string[] ExchangeMemberNames =
        ["PrimarySmtpAddress", "ExternalDirectoryObjectId", "Guid", "WindowsLiveID", "MicrosoftOnlineServicesID", "Identity", "Alias", "Name"];

    /// <summary>The rows of the arguments: one group and the users.</summary>
    /// <exception cref="AegisException">A usage error: no group or no user.</exception>
    public static IReadOnlyList<GroupChangeRow> FromArguments(string? group, IReadOnlyList<string>? users)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            throw new AegisException(FailureKind.Usage, "no group was named", "give the group and the users, or --from-csv with the columns group,user");
        }

        List<string> named = (users ?? []).Where(user => !string.IsNullOrWhiteSpace(user)).Select(user => user.Trim()).ToList();
        return named.Count == 0
            ? throw new AegisException(FailureKind.Usage, "no user was named", "give one or more user principal names or object ids after the group")
            : named.Select(user => new GroupChangeRow(group.Trim(), user)).ToList();
    }

    /// <summary>The rows of a CSV file with the columns <c>group,user</c>, one change per line (task 054).</summary>
    /// <exception cref="AegisException">A usage error naming the file and the line.</exception>
    public static IReadOnlyList<GroupChangeRow> FromCsv(string path) =>
        CsvRows.Read(path, CsvColumns, []).Select(row => new GroupChangeRow(row["group"]!, row["user"]!, row.Line)).ToList();

    /// <summary>
    /// The plan of a member or owner write over the rows, over Graph alone: a distribution or mail-enabled security
    /// group's members are planned as Graph changes the service will refuse, with a warning that says so.
    /// </summary>
    /// <param name="graph">The client.</param>
    /// <param name="relation">The members or the owners.</param>
    /// <param name="add">Add the users, or remove them.</param>
    /// <param name="rows">What to change, from the arguments or a CSV file.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="AegisException">A group or a user to add that does not exist, or the failure of a read; nothing was sent.</exception>
    public static Task<WritePlan> PlanAsync(GraphClient graph, GroupRelation relation, bool add, IReadOnlyList<GroupChangeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return PlanAsync(graph, null, relation, add, rows, cancellationToken);
    }

    /// <summary>
    /// The plan of a member or owner write over the rows, over the layers of the run (task 061): Graph for every group
    /// and for the owners, Exchange for the members of a distribution or mail-enabled security group.
    /// </summary>
    /// <param name="backends">The layers: Graph, the Admin API and the sidecar.</param>
    /// <param name="relation">The members or the owners.</param>
    /// <param name="add">Add the users, or remove them.</param>
    /// <param name="rows">What to change, from the arguments or a CSV file.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="AegisException">A group or a user to add that does not exist, or the failure of a read; nothing was sent.</exception>
    public static Task<WritePlan> PlanAsync(ExchangeBackends backends, GroupRelation relation, bool add, IReadOnlyList<GroupChangeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return PlanAsync(backends.Graph, backends, relation, add, rows, cancellationToken);
    }

    private static async Task<WritePlan> PlanAsync(
        GraphClient graph, ExchangeBackends? exchange, GroupRelation relation, bool add, IReadOnlyList<GroupChangeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new AegisException(FailureKind.Usage, "no change was asked for", "give the group and the users, or --from-csv");
        }

        string list = relation == GroupRelation.Members ? "members" : "owners";
        var groups = new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in rows.Select(row => row.Group).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            groups[name] = await GroupLookups.ResolveAsync(graph, name, cancellationToken).ConfigureAwait(false);
        }

        var current = new Dictionary<string, IReadOnlyList<Listed>>(StringComparer.OrdinalIgnoreCase);
        foreach (GroupInfo group in groups.Values.DistinctBy(group => group.Id, StringComparer.OrdinalIgnoreCase))
        {
            current[group.Id] = exchange is not null && ThroughExchange(relation, group)
                ? await ExchangeMembersAsync(graph, exchange, group, cancellationToken).ConfigureAwait(false)
                : (await GroupLookups.ListAsync(graph, group.Id, list, cancellationToken).ConfigureAwait(false)).Select(Listed.FromGraph).ToList();
        }

        IReadOnlyDictionary<string, GroupMember> resolved = add
            ? await ResolveAsync(graph, rows.Where(row => Match(current[groups[row.Group].Id], row.User) is null).ToList(), cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, GroupMember>();

        string role = relation == GroupRelation.Members ? "a member" : "an owner";
        string not = relation == GroupRelation.Members ? "not a member" : "not an owner";
        string permission = relation == GroupRelation.Members ? MemberWritePermission : OwnerWritePermission;
        var builder = new PlanBuilder(Operation(relation, add), "");
        PlanBatch<GraphRequest> batch = RefBatch(graph);
        var planned = new HashSet<(string Group, string User)>();
        var removed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<GroupInfo>();
        foreach (GroupChangeRow row in rows)
        {
            GroupInfo group = groups[row.Group];
            IReadOnlyList<Listed> members = current[group.Id];
            GroupMember? known = add && resolved.TryGetValue(row.User, out GroupMember? user) ? user : null;
            Listed? found = Match(members, row.User) ?? (known is null ? null : Match(members, known.Id) ?? Match(members, known.Mail) ?? Match(members, known.UserPrincipalName));
            if (add && found is not null)
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, $"{Describe(found.Member)} as {role}", $"{role} already") { Row = row.Row });
                continue;
            }

            if (!add && found is null)
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, $"{row.User} as {role}", not) { Row = row.Row });
                continue;
            }

            GroupMember subject = found?.Member ?? known!;
            string detail = $"{Describe(subject)} as {role}";
            if (!planned.Add((group.Id.ToUpperInvariant(), subject.Id.ToUpperInvariant())))
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, detail, "named twice: the change is planned once") { Row = row.Row });
                continue;
            }

            var change = new PlanChange(add ? PlanAction.Add : PlanAction.Remove, group.DisplayName, detail, add ? not : role, add ? role : not) { Row = row.Row };
            if (exchange is not null && ThroughExchange(relation, group))
            {
                ExoCommand command = MemberCommand(add, group, found?.ExchangeIdentity ?? subject.Mail ?? subject.UserPrincipalName ?? subject.Id);
                builder.Change(change, PlanStep.Alone(async ct => await exchange.Sidecar.InvokeAsync(command, ct).ConfigureAwait(false)));
            }
            else
            {
                GraphRequest request = add
                    ? new GraphRequest(HttpMethod.Post, $"/groups/{Uri.EscapeDataString(group.Id)}/{list}/$ref")
                    {
                        Body = JsonSerializer.Serialize(new Dictionary<string, string> { ["@odata.id"] = $"{GraphClient.BaseUrl}/v1.0/directoryObjects/{subject.Id}" }),
                        Permission = permission,
                    }
                    : new GraphRequest(HttpMethod.Delete, $"/groups/{Uri.EscapeDataString(group.Id)}/{list}/{Uri.EscapeDataString(subject.Id)}/$ref") { Permission = permission };
                builder.Change(change, PlanStep.Batched(batch, request));
            }

            if (!add)
            {
                removed[group.Id] = removed.GetValueOrDefault(group.Id) + 1;
            }

            if (!changed.Contains(group))
            {
                changed.Add(group);
            }
        }

        foreach (GroupInfo group in changed)
        {
            Warn(builder, relation, group, removed.GetValueOrDefault(group.Id), current[group.Id].Count, exchange is not null);
        }

        List<GroupInfo> named = groups.Values.DistinctBy(group => group.Id, StringComparer.OrdinalIgnoreCase).ToList();
        builder.Title = Title(relation, add, named);
        return builder.Build();
    }

    /// <summary>The operation's stable name: <c>group-members-add</c>, <c>group-owners-remove</c>, ….</summary>
    public static string Operation(GroupRelation relation, bool add) =>
        $"group-{(relation == GroupRelation.Members ? "members" : "owners")}-{(add ? "add" : "remove")}";

    /// <summary>
    /// The batch the <c>$ref</c> writes of one plan go through (task 058): twenty to a <c>$batch</c> call
    /// (<see cref="GraphClient.MaxBatchSize"/>), each change's outcome read from its own part of the answer, the same
    /// failure a single request would have been (a 403 names the permission the change needs).
    /// </summary>
    internal static PlanBatch<GraphRequest> RefBatch(GraphClient graph) =>
        new(GraphClient.MaxBatchSize, async (requests, cancellationToken) =>
        {
            IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, cancellationToken).ConfigureAwait(false);
            return answers.Select(Outcome).ToList();
        });

    private static AegisFailure? Outcome(GraphBatchResponse answer)
    {
        try
        {
            answer.EnsureSuccess();
            return null;
        }
        catch (AegisException exception)
        {
            return exception.Failure;
        }
    }

    /// <summary>Whether Exchange manages the list: the members of a distribution or mail-enabled security group that is not synced.</summary>
    private static bool ThroughExchange(GroupRelation relation, GroupInfo group) =>
        relation == GroupRelation.Members && !group.Synced && group.Kind is GroupKind.Distribution or GroupKind.MailEnabledSecurity;

    /// <summary>
    /// A distribution or mail-enabled security group's members as Exchange keeps them (task 061): the Admin API's
    /// <c>Get-DistributionGroupMember</c> over every page, the hidden members included; Graph's list when the run has no
    /// Admin API or it cannot answer for a reason other than the sign-in.
    /// </summary>
    private static async Task<IReadOnlyList<Listed>> ExchangeMembersAsync(GraphClient graph, ExchangeBackends exchange, GroupInfo group, CancellationToken cancellationToken)
    {
        if (exchange.HasAdminApi)
        {
            try
            {
                ExoResult result = await exchange.AdminApi!.InvokeAsync(
                    new ExoCommand(AdminApiCmdlets.GetDistributionGroupMember)
                    {
                        Parameters = new Dictionary<string, object?> { ["Identity"] = ExchangeIdentity(group), ["ResultSize"] = "Unlimited" },
                    },
                    cancellationToken).ConfigureAwait(false);
                return result.Items.Select(Listed.FromExchange).ToList();
            }
            catch (AegisException exception) when (exception.Failure.Kind is not (FailureKind.Auth or FailureKind.Usage))
            {
                LogSink.Verbose?.Write("admin-api", $"the Admin API could not list the members of a distribution group ({exception.Failure.Code}: {exception.Message}); Graph's list is used instead");
            }
        }

        return (await GroupLookups.ListAsync(graph, group.Id, "members", cancellationToken).ConfigureAwait(false)).Select(Listed.FromGraph).ToList();
    }

    /// <summary>The group as Exchange takes it: its primary address, else its object id.</summary>
    private static string ExchangeIdentity(GroupInfo group) => group.Mail ?? group.Id;

    /// <summary>The cmdlet of one member change: <c>Add-</c> or <c>Remove-DistributionGroupMember</c>, without a prompt.</summary>
    private static ExoCommand MemberCommand(bool add, GroupInfo group, string member) =>
        new(add ? ExoCmdlets.AddDistributionGroupMember : ExoCmdlets.RemoveDistributionGroupMember)
        {
            Parameters = new Dictionary<string, object?>
            {
                ["Identity"] = ExchangeIdentity(group),
                ["Member"] = member,
                // An administrator who is not among the group's owners (ManagedBy) needs it; the Organization Management
                // role group has the role it asks for.
                ["BypassSecurityGroupManagerCheck"] = true,
                ["Confirm"] = false,
            },
        };

    private static void Warn(PlanBuilder builder, GroupRelation relation, GroupInfo group, int removing, int count, bool exchange)
    {
        string kind = group.Kind switch
        {
            GroupKind.Distribution => "a distribution group",
            GroupKind.MailEnabledSecurity => "a mail-enabled security group",
            _ => "a group",
        };
        if (group.Synced)
        {
            builder.Warn($"{group.DisplayName} is synced from on-premises Active Directory: Graph cannot change its {(relation == GroupRelation.Members ? "members" : "owners")} there, so the service will refuse the change");
        }
        else if (relation == GroupRelation.Members && group.Kind is GroupKind.Distribution or GroupKind.MailEnabledSecurity && !exchange)
        {
            builder.Warn($"{group.DisplayName} is {kind}: Exchange manages its members and Graph cannot change them, so the service will refuse the change");
        }
        else if (relation == GroupRelation.Owners && !group.OwnersInGraph)
        {
            builder.Warn($"{group.DisplayName} is {kind}: Graph keeps no owners for it, so the service will refuse the change");
        }

        if (relation == GroupRelation.Owners && removing > 0 && removing >= count)
        {
            builder.Warn($"{group.DisplayName} loses its last owner: only an administrator can manage it afterwards, and `group owners-missing` lists it");
        }
    }

    private static string Title(GroupRelation relation, bool add, List<GroupInfo> groups)
    {
        string what = relation == GroupRelation.Members ? "members" : "owners";
        string verb = add ? $"Add {what} to" : $"Remove {what} from";
        return groups.Count == 1
            ? $"{verb} {groups[0].DisplayName} ({groups[0].Id})"
            : string.Format(CultureInfo.InvariantCulture, "{0} {1} groups", verb, groups.Count);
    }

    /// <summary>The member a user as given names: any name the list knows it by (the object id, the sign-in name, an address).</summary>
    private static Listed? Match(IReadOnlyList<Listed> members, string? user) =>
        string.IsNullOrWhiteSpace(user) ? null : members.FirstOrDefault(member => member.Names.Contains(user));

    /// <summary>A member or a user as a change names it: the name and the sign-in name, or the type and the id.</summary>
    private static string Describe(GroupMember member) =>
        (member.UserPrincipalName ?? member.Mail) is { } address
            ? $"{member.DisplayName ?? member.Id} ({address})"
            : $"{member.DisplayName ?? member.Id} ({member.Type}, {member.Id})";

    /// <summary>
    /// The users to add, by the name they were given: a user principal name through <c>/users</c>, an object id
    /// through <c>/directoryObjects</c> (a group, a device, a service principal too), twenty to a <c>$batch</c> call.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, GroupMember>> ResolveAsync(GraphClient graph, IReadOnlyList<GroupChangeRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, GroupMember>(StringComparer.OrdinalIgnoreCase);
        List<GroupChangeRow> distinct = rows.DistinctBy(row => row.User, StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0)
        {
            return result;
        }

        List<GraphRequest> requests = distinct.Select(row => Guid.TryParse(row.User, out Guid id)
                ? GraphRequest.Get($"/directoryObjects/{id:D}") with { Permission = UserReadPermission }
                : GraphRequest.Get(GraphQuery.Path($"/users/{Uri.EscapeDataString(row.User)}", ("$select", UserFields))) with { Permission = UserReadPermission })
            .ToList();
        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < distinct.Count; index++)
        {
            GroupChangeRow row = distinct[index];
            GraphBatchResponse answer = answers[index];
            if (answer.StatusCode is 404 or 400)
            {
                string where = row.Row is { } line ? string.Format(CultureInfo.InvariantCulture, " (line {0})", line) : "";
                throw new AegisException(
                    FailureKind.NotFound,
                    $"no user or object '{row.User}' in the directory{where}",
                    "give the user principal name or the object id; nothing was sent");
            }

            answer.EnsureSuccess();
            GraphDirectoryObject found = answer.Deserialize<GraphDirectoryObject>() ?? new GraphDirectoryObject { Id = row.User };
            GroupMember member = found.ToMember();
            // A user read through /users with $select carries no @odata.type.
            result[row.User] = member.Type == "directoryObject" && !Guid.TryParse(row.User, out _) ? member with { Type = "user" } : member;
        }

        return result;
    }

    /// <summary>
    /// A member on a group's current list with every name it may be given by: Graph's object id, sign-in name and
    /// address; or Exchange's addresses, object ids, alias and name, and the identity a member cmdlet takes.
    /// </summary>
    private sealed class Listed(GroupMember member, IEnumerable<string?> names, string? exchangeIdentity = null)
    {
        internal GroupMember Member { get; } = member;

        internal HashSet<string> Names { get; } = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>How <c>Remove-DistributionGroupMember -Member</c> names it; null for a Graph member.</summary>
        internal string? ExchangeIdentity { get; } = exchangeIdentity;

        internal static Listed FromGraph(GroupMember member) => new(member, [member.Id, member.UserPrincipalName, member.Mail]);

        internal static Listed FromExchange(ExoObject item)
        {
            string? address = item.Text("PrimarySmtpAddress");
            string? id = item.Text("ExternalDirectoryObjectId") ?? item.Text("Guid");
            string type = item.Text("RecipientTypeDetails") switch
            {
                null => "recipient",
                { } details when details.Contains("Group", StringComparison.OrdinalIgnoreCase) => "group",
                { } details when details.Contains("Contact", StringComparison.OrdinalIgnoreCase) => "contact",
                { } details when details.Contains("Mailbox", StringComparison.OrdinalIgnoreCase) || details.Contains("User", StringComparison.OrdinalIgnoreCase) => "user",
                _ => "recipient",
            };
            var member = new GroupMember(type, item.Text("DisplayName") ?? item.Text("Name"), item.Text("WindowsLiveID") ?? item.Text("MicrosoftOnlineServicesID"), address, id ?? address ?? item.Text("Identity") ?? "?");
            IEnumerable<string?> proxies = item.Texts("EmailAddresses")
                .Where(proxy => proxy.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase))
                .Select(proxy => proxy[5..]);
            return new Listed(member, ExchangeMemberNames.Select(item.Text).Concat(proxies), address ?? id ?? item.Text("Identity") ?? item.Text("Name"));
        }
    }
}
