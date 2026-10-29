using System.Globalization;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Groups;

/// <summary>
/// The read lookups of the groups domain over Graph (task 005): a group by id, name or address, its members, its
/// owners, and every group without an owner. Each request names the delegated permission it needs, so a 403
/// says which one to consent.
/// </summary>
public static class GroupLookups
{
    /// <summary>The permission the group reads need: the groups, their members and owners.</summary>
    public const string Permission = "GroupMember.Read.All";

    /// <summary>The fields every group request selects; one shape for the resolution and the lists.</summary>
    internal const string GroupFields = "id,displayName,mail,groupTypes,securityEnabled,onPremisesSyncEnabled";

    private const string MemberFields = "id,displayName,userPrincipalName,mail";
    private const int Candidates = 10;

    /// <summary>
    /// The group an argument names: an object id; else a group whose display name (or, with an <c>@</c>, address)
    /// is exactly the argument; else the one group whose name <c>$search</c> matches. No match, or more than one,
    /// is a <see cref="FailureKind.NotFound"/> failure whose hint lists the candidates with their ids.
    /// </summary>
    public static async Task<GroupInfo> ResolveAsync(GraphClient graph, string nameOrId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string argument = (nameOrId ?? "").Trim();
        if (argument.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no group was named", "give the group's name, address or object id");
        }

        if (Guid.TryParse(argument, out Guid id))
        {
            string path = GraphQuery.Path($"/groups/{id:D}", ("$select", GroupFields));
            try
            {
                GraphGroup? group = await graph.GetAsync<GraphGroup>(GraphRequest.Get(path) with { Permission = Permission }, cancellationToken).ConfigureAwait(false);
                return group?.ToInfo() ?? throw NoGroup(argument);
            }
            catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
            {
                throw NoGroup(argument);
            }
        }

        string field = argument.Contains('@', StringComparison.Ordinal) ? "mail" : "displayName";
        IReadOnlyList<GroupInfo> exact = await FindAsync(graph, ("$filter", $"{field} eq {GraphQuery.Literal(argument)}"), cancellationToken).ConfigureAwait(false);
        if (exact.Count == 1)
        {
            return exact[0];
        }

        if (exact.Count > 1)
        {
            throw Ambiguous(argument, exact);
        }

        // $search matches the words of the name: "fin" finds "Finance" and "Finance EU". The quotes are the
        // syntax's own, so a quote in the argument is dropped.
        string words = argument.Replace("\"", "", StringComparison.Ordinal);
        IReadOnlyList<GroupInfo> found = await FindAsync(graph, ("$search", $"\"displayName:{words}\""), cancellationToken).ConfigureAwait(false);
        return found.Count switch
        {
            1 => found[0],
            0 => throw NoGroup(argument),
            _ => throw Ambiguous(argument, found),
        };
    }

    /// <summary><c>group members</c>: the group's direct members, by name.</summary>
    public static async Task<CommandResult<GroupMembers>> MembersAsync(GraphClient graph, string nameOrId, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupMember> members = await ListAsync(graph, group, "members", cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupMembers(group, members));
    }

    /// <summary><c>group owners</c>: the group's owners, by name.</summary>
    public static async Task<CommandResult<GroupOwners>> OwnersAsync(GraphClient graph, string nameOrId, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupMember> owners = await ListAsync(graph, group, "owners", cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupOwners(group, owners));
    }

    /// <summary>
    /// <c>group owners-missing</c>: every group without an owner, in one paged advanced query
    /// (<c>$filter=owners/$count eq 0</c>), not a request per group. Graph keeps no owners for distribution,
    /// mail-enabled security and synced groups, so they are left out and counted unless
    /// <paramref name="allTypes"/> is set.
    /// </summary>
    public static async Task<CommandResult<OwnerlessGroups>> OwnerlessAsync(GraphClient graph, bool allTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string path = GraphQuery.Path(
            "/groups",
            ("$filter", "owners/$count eq 0"),
            ("$count", "true"),
            ("$select", GroupFields),
            ("$top", "999"));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        IReadOnlyList<GraphGroup> groups = await graph.ListAsync<GraphGroup>(request, cancellationToken).ConfigureAwait(false);
        List<GroupInfo> all = groups.Select(group => group.ToInfo()).OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        List<GroupInfo> shown = allTypes ? all : all.Where(group => group.OwnersInGraph).ToList();
        return CommandResult.Success(new OwnerlessGroups(shown, all.Count - shown.Count));
    }

    private static async Task<IReadOnlyList<GroupInfo>> FindAsync(GraphClient graph, (string Name, string? Value) query, CancellationToken cancellationToken)
    {
        string path = GraphQuery.Path(
            "/groups",
            query,
            ("$count", "true"),
            ("$select", GroupFields),
            ("$top", Candidates.ToString(CultureInfo.InvariantCulture)));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        GraphPage<GraphGroup> page = await graph.GetAsync<GraphPage<GraphGroup>>(request, cancellationToken).ConfigureAwait(false) ?? new GraphPage<GraphGroup>();
        return page.Value.Select(group => group.ToInfo()).ToList();
    }

    private static async Task<IReadOnlyList<GroupMember>> ListAsync(GraphClient graph, GroupInfo group, string relation, CancellationToken cancellationToken)
    {
        // $select on the members and owners lists is an advanced query: ConsistencyLevel and $count go with it.
        string path = GraphQuery.Path(
            $"/groups/{Uri.EscapeDataString(group.Id)}/{relation}",
            ("$count", "true"),
            ("$select", MemberFields),
            ("$top", "999"));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        IReadOnlyList<GraphDirectoryObject> objects = await graph.ListAsync<GraphDirectoryObject>(request, cancellationToken).ConfigureAwait(false);
        return objects
            .Select(item => item.ToMember())
            .OrderBy(member => member.DisplayName ?? member.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static AegisException NoGroup(string argument) => new(
        FailureKind.NotFound,
        $"no group named '{argument}'",
        "give the exact display name, the address, the object id, or a word of the name");

    private static AegisException Ambiguous(string argument, IReadOnlyList<GroupInfo> candidates)
    {
        string list = string.Join(", ", candidates.Select(group => $"{group.DisplayName} ({group.Id})"));
        string more = candidates.Count >= Candidates ? ", and maybe more" : "";
        return new AegisException(
            FailureKind.NotFound,
            $"'{argument}' names {candidates.Count.ToString(CultureInfo.InvariantCulture)} groups{more}",
            $"give the object id of one: {list}");
    }
}
