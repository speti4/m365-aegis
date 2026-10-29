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

    /// <summary>
    /// <c>group members [--transitive]</c>: the group's direct members, by name; with <paramref name="transitive"/>
    /// every member of the nested groups too, from <c>/groups/{id}/transitiveMembers</c> (task 033), each marked
    /// direct or nested. The transitive list does not say through which group a member came, so the direct list is
    /// asked for beside it and a member missing from it is a nested one.
    /// </summary>
    public static async Task<CommandResult<GroupMembers>> MembersAsync(GraphClient graph, string nameOrId, bool transitive, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        if (!transitive)
        {
            IReadOnlyList<GroupMember> members = await ListAsync(graph, group, "members", cancellationToken).ConfigureAwait(false);
            return CommandResult.Success(new GroupMembers(group, members));
        }

        Task<IReadOnlyList<GroupMember>> direct = ListAsync(graph, group, "members", cancellationToken);
        Task<IReadOnlyList<GroupMember>> all = ListAsync(graph, group, "transitiveMembers", cancellationToken);
        await Task.WhenAll(direct, all).ConfigureAwait(false);
        var directIds = new HashSet<string>((await direct.ConfigureAwait(false)).Select(member => member.Id), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<GroupMember> marked = (await all.ConfigureAwait(false))
            .Select(member => member with { Nested = !directIds.Contains(member.Id) })
            .ToList();
        return CommandResult.Success(new GroupMembers(group, marked, Transitive: true));
    }

    /// <summary><c>group owners</c>: the group's owners, by name.</summary>
    public static async Task<CommandResult<GroupOwners>> OwnersAsync(GraphClient graph, string nameOrId, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupMember> owners = await ListAsync(graph, group, "owners", cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupOwners(group, owners));
    }

    /// <summary>The advanced filter of every group without an owner.</summary>
    internal const string OwnerlessFilter = "owners/$count eq 0";

    /// <summary>The Microsoft 365 groups without an owner (the documented filter on <c>groupTypes</c>).</summary>
    internal const string OwnerlessMicrosoft365Filter = "groupTypes/any(c:c eq 'Unified') and owners/$count eq 0";

    /// <summary>The security groups without mail and without an owner (the documented security group filter).</summary>
    internal const string OwnerlessSecurityFilter = "mailEnabled eq false and securityEnabled eq true and NOT(groupTypes/any(c:c eq 'Unified')) and owners/$count eq 0";

    /// <summary>
    /// <c>group owners-missing</c>: every group without an owner, by advanced queries on <c>owners/$count eq 0</c>,
    /// never a request per group. Graph keeps no owners for distribution, mail-enabled security and synced groups,
    /// so by default only the two kinds it keeps them for are asked for, side by side: the Microsoft 365 groups and
    /// the security groups without mail, each one paged query (task 032: the left-out kinds are most of a tenant's
    /// ownerless groups and filled the pages of the one wide query), with a <c>$count</c> of every ownerless group
    /// sent next to them for the left-out number (a raw <c>$count</c>, accepted as <c>text/plain</c>); the synced ones
    /// are picked out here. Should Graph refuse a narrowed filter or the count (400, 406, 415), the wide query answers
    /// instead. <paramref name="allTypes"/> lists every kind from the wide query.
    /// </summary>
    public static async Task<CommandResult<OwnerlessGroups>> OwnerlessAsync(GraphClient graph, bool allTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!allTypes)
        {
            Task<IReadOnlyList<GraphGroup>> microsoft365 = graph.ListAsync<GraphGroup>(OwnerlessRequest(OwnerlessMicrosoft365Filter), cancellationToken);
            Task<IReadOnlyList<GraphGroup>> security = graph.ListAsync<GraphGroup>(OwnerlessRequest(OwnerlessSecurityFilter), cancellationToken);
            Task<long> total = graph.GetAsync<long>(OwnerlessCountRequest(), cancellationToken);
            try
            {
                await Task.WhenAll(microsoft365, security, total).ConfigureAwait(false);
                IReadOnlyList<GraphGroup> found = [.. await microsoft365.ConfigureAwait(false), .. await security.ConfigureAwait(false)];
                List<GroupInfo> shown = Sorted(found).Where(group => group.OwnersInGraph).ToList();
                long ownerless = await total.ConfigureAwait(false);
                return CommandResult.Success(new OwnerlessGroups(shown, (int)Math.Max(0, ownerless - shown.Count)));
            }
            catch (AegisException exception) when (exception.HttpStatus is 400 or 406 or 415)
            {
                // A narrowed filter or the count Graph does not take: the wide query below answers the same question.
            }
        }

        IReadOnlyList<GraphGroup> groups = await graph.ListAsync<GraphGroup>(OwnerlessRequest(OwnerlessFilter), cancellationToken).ConfigureAwait(false);
        List<GroupInfo> all = Sorted(groups);
        List<GroupInfo> listed = allTypes ? all : all.Where(group => group.OwnersInGraph).ToList();
        return CommandResult.Success(new OwnerlessGroups(listed, all.Count - listed.Count));
    }

    /// <summary>The paged advanced query of the groups a filter names, at the largest page.</summary>
    internal static GraphRequest OwnerlessRequest(string filter)
    {
        string path = GraphQuery.Path("/groups", ("$filter", filter), ("$count", "true"), ("$select", GroupFields), ("$top", "999"));
        return GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
    }

    /// <summary>The count of every group without an owner: <c>/groups/$count</c>, a number and no page.</summary>
    internal static GraphRequest OwnerlessCountRequest() =>
        GraphRequest.Get(GraphQuery.Path("/groups/$count", ("$filter", OwnerlessFilter))) with
        {
            EventualConsistency = true,
            Permission = Permission,
            Accept = GraphRequest.PlainText,
        };

    private static List<GroupInfo> Sorted(IEnumerable<GraphGroup> groups) =>
        groups.Select(group => group.ToInfo()).OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Id, StringComparer.Ordinal).ToList();

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
