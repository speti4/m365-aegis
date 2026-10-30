using System.Globalization;
using System.Text.Json;
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
/// The writes of the groups domain over Graph (task 052): <c>group members add|remove</c> and <c>group owners
/// add|remove</c>, each a <see cref="WritePlan"/>. The plan reads the group's current list (the lookups of task 005)
/// and lists only what is not so already: a member already there, or one to remove who is not, is left alone. A user
/// to add is resolved first (a user principal name through <c>/users</c>, an object id through
/// <c>/directoryObjects</c>, twenty to a <c>$batch</c> call), and one that does not exist fails the whole plan before
/// anything is sent. A change is one <c>$ref</c> request: <c>POST /groups/{id}/members/$ref</c> with the object's
/// <c>@odata.id</c>, <c>DELETE /groups/{id}/members/{object id}/$ref</c>, the owners alike. The plan warns when a
/// group would lose its last owner, and when Graph cannot change the list (a distribution or mail-enabled security
/// group, a synced one: Exchange or on-premises Active Directory manages it).
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

    /// <summary>The plan of a member or owner write over the rows.</summary>
    /// <param name="graph">The client.</param>
    /// <param name="relation">The members or the owners.</param>
    /// <param name="add">Add the users, or remove them.</param>
    /// <param name="rows">What to change, from the arguments or a CSV file.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="AegisException">A group or a user to add that does not exist, or the failure of a read; nothing was sent.</exception>
    public static async Task<WritePlan> PlanAsync(GraphClient graph, GroupRelation relation, bool add, IReadOnlyList<GroupChangeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
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

        var current = new Dictionary<string, IReadOnlyList<GroupMember>>(StringComparer.OrdinalIgnoreCase);
        foreach (GroupInfo group in groups.Values.DistinctBy(group => group.Id, StringComparer.OrdinalIgnoreCase))
        {
            current[group.Id] = await GroupLookups.ListAsync(graph, group.Id, list, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyDictionary<string, GroupMember> resolved = add
            ? await ResolveAsync(graph, rows.Where(row => Match(current[groups[row.Group].Id], row.User) is null).ToList(), cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, GroupMember>();

        string role = relation == GroupRelation.Members ? "a member" : "an owner";
        string not = relation == GroupRelation.Members ? "not a member" : "not an owner";
        string permission = relation == GroupRelation.Members ? MemberWritePermission : OwnerWritePermission;
        var builder = new PlanBuilder(Operation(relation, add), "");
        var planned = new HashSet<(string Group, string User)>();
        var removed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<GroupInfo>();
        foreach (GroupChangeRow row in rows)
        {
            GroupInfo group = groups[row.Group];
            IReadOnlyList<GroupMember> members = current[group.Id];
            GroupMember? found = Match(members, row.User) ?? (add && resolved.TryGetValue(row.User, out GroupMember? known) ? Match(members, known.Id) : null);
            if (add && found is not null)
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, $"{Describe(found)} as {role}", $"{role} already") { Row = row.Row });
                continue;
            }

            if (!add && found is null)
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, $"{row.User} as {role}", not) { Row = row.Row });
                continue;
            }

            GroupMember subject = found ?? resolved[row.User];
            string detail = $"{Describe(subject)} as {role}";
            if (!planned.Add((group.Id.ToUpperInvariant(), subject.Id.ToUpperInvariant())))
            {
                builder.Unchanged(new PlanUnchanged(group.DisplayName, detail, "named twice: the change is planned once") { Row = row.Row });
                continue;
            }

            GraphRequest request = add
                ? new GraphRequest(HttpMethod.Post, $"/groups/{Uri.EscapeDataString(group.Id)}/{list}/$ref")
                {
                    Body = JsonSerializer.Serialize(new Dictionary<string, string> { ["@odata.id"] = $"{GraphClient.BaseUrl}/v1.0/directoryObjects/{subject.Id}" }),
                    Permission = permission,
                }
                : new GraphRequest(HttpMethod.Delete, $"/groups/{Uri.EscapeDataString(group.Id)}/{list}/{Uri.EscapeDataString(subject.Id)}/$ref") { Permission = permission };
            builder.Change(
                new PlanChange(add ? PlanAction.Add : PlanAction.Remove, group.DisplayName, detail, add ? not : role, add ? role : not) { Row = row.Row },
                async ct => await graph.SendAsync(request, ct).ConfigureAwait(false));
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
            Warn(builder, relation, group, removed.GetValueOrDefault(group.Id), current[group.Id].Count);
        }

        List<GroupInfo> named = groups.Values.DistinctBy(group => group.Id, StringComparer.OrdinalIgnoreCase).ToList();
        builder.Title = Title(relation, add, named);
        return builder.Build();
    }

    /// <summary>The operation's stable name: <c>group-members-add</c>, <c>group-owners-remove</c>, ….</summary>
    public static string Operation(GroupRelation relation, bool add) =>
        $"group-{(relation == GroupRelation.Members ? "members" : "owners")}-{(add ? "add" : "remove")}";

    private static void Warn(PlanBuilder builder, GroupRelation relation, GroupInfo group, int removing, int count)
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
        else if (relation == GroupRelation.Members && group.Kind is GroupKind.Distribution or GroupKind.MailEnabledSecurity)
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

    /// <summary>The member a user as given names: the object id, the user principal name or the address.</summary>
    private static GroupMember? Match(IReadOnlyList<GroupMember> members, string user) =>
        members.FirstOrDefault(member =>
            string.Equals(member.Id, user, StringComparison.OrdinalIgnoreCase)
            || string.Equals(member.UserPrincipalName, user, StringComparison.OrdinalIgnoreCase)
            || string.Equals(member.Mail, user, StringComparison.OrdinalIgnoreCase));

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
}
