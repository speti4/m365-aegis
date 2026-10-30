using System.Globalization;
using M365Aegis.Core.Exchange;
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
    public static Task<CommandResult<GroupMembers>> MembersAsync(GraphClient graph, string nameOrId, bool transitive, CancellationToken cancellationToken) =>
        MembersAsync(graph, nameOrId, transitive, via: false, cancellationToken);

    /// <summary>
    /// <c>group members [--transitive] [--via]</c>: as <see cref="MembersAsync(GraphClient, string, bool, CancellationToken)"/>;
    /// <paramref name="via"/> (which implies <paramref name="transitive"/>, task 041) also names, on each nested member,
    /// the direct member group it is reached through: the direct members of every group of the transitive list are
    /// read with one <c>members</c> request per group, twenty to a <c>$batch</c> call, and followed from each direct
    /// member group down. The plain transitive list stays the two lists beside the resolution.
    /// </summary>
    public static async Task<CommandResult<GroupMembers>> MembersAsync(GraphClient graph, string nameOrId, bool transitive, bool via, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        if (!transitive && !via)
        {
            IReadOnlyList<GroupMember> members = await ListAsync(graph, group.Id, "members", cancellationToken).ConfigureAwait(false);
            return CommandResult.Success(new GroupMembers(group, members));
        }

        IReadOnlyList<GroupMember> marked = await TransitiveAsync(graph, group.Id, via, cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupMembers(group, marked, Transitive: true) { Via = via ? true : null });
    }

    /// <summary>
    /// The transitive members of a group by its object id, each marked direct or nested, and with
    /// <paramref name="via"/> each nested one with the direct member groups it is reached through (task 041).
    /// </summary>
    internal static async Task<IReadOnlyList<GroupMember>> TransitiveAsync(GraphClient graph, string groupId, bool via, CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<GroupMember>> direct = ListAsync(graph, groupId, "members", cancellationToken);
        Task<IReadOnlyList<GroupMember>> all = ListAsync(graph, groupId, "transitiveMembers", cancellationToken);
        await Task.WhenAll(direct, all).ConfigureAwait(false);
        IReadOnlyList<GroupMember> directMembers = await direct.ConfigureAwait(false);
        IReadOnlyList<GroupMember> allMembers = await all.ConfigureAwait(false);
        if (!via)
        {
            return Mark(directMembers, allMembers, null);
        }

        IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> membersOf =
            await MembersOfAsync(graph, allMembers.Where(member => member.Type == "group").Select(member => member.Id), cancellationToken).ConfigureAwait(false);
        return Mark(directMembers, allMembers, membersOf);
    }

    /// <summary>
    /// The transitive list with each member marked direct or nested; with <paramref name="membersOf"/> (the direct
    /// members of every group of the list) each nested member also names the direct member groups whose nesting
    /// reaches it, followed down from each of them (a group met twice is followed once, so a cycle ends).
    /// </summary>
    internal static IReadOnlyList<GroupMember> Mark(
        IReadOnlyList<GroupMember> direct,
        IReadOnlyList<GroupMember> all,
        IReadOnlyDictionary<string, IReadOnlyList<GroupMember>>? membersOf)
    {
        var directIds = new HashSet<string>(direct.Select(member => member.Id), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<MemberGroup>>? reached = membersOf is null ? null : Reach(direct, membersOf);
        return all
            .Select(member =>
            {
                bool nested = !directIds.Contains(member.Id);
                IReadOnlyList<MemberGroup>? groups = null;
                if (nested && reached is not null)
                {
                    groups = reached.TryGetValue(member.Id, out List<MemberGroup>? found)
                        ? found.OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Id, StringComparer.Ordinal).ToList()
                        : [];
                }

                return member with { Nested = nested, Via = groups };
            })
            .ToList();
    }

    /// <summary>For every object under a direct member group, the direct member groups whose nesting reaches it.</summary>
    private static Dictionary<string, List<MemberGroup>> Reach(IReadOnlyList<GroupMember> direct, IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> membersOf)
    {
        var reached = new Dictionary<string, List<MemberGroup>>(StringComparer.OrdinalIgnoreCase);
        foreach (GroupMember top in direct.Where(member => member.Type == "group"))
        {
            var via = new MemberGroup(top.Id, top.DisplayName ?? top.Id);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { top.Id };
            var pending = new Queue<string>([top.Id]);
            while (pending.Count > 0)
            {
                if (!membersOf.TryGetValue(pending.Dequeue(), out IReadOnlyList<GroupMember>? members))
                {
                    continue;
                }

                foreach (GroupMember member in members)
                {
                    if (!reached.TryGetValue(member.Id, out List<MemberGroup>? list))
                    {
                        list = [];
                        reached[member.Id] = list;
                    }

                    if (!list.Contains(via))
                    {
                        list.Add(via);
                    }

                    if (member.Type == "group" && seen.Add(member.Id))
                    {
                        pending.Enqueue(member.Id);
                    }
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// The direct members of each group, by its object id: one <c>members</c> request per group, twenty to a
    /// <c>$batch</c> call (<see cref="GraphClient.BatchAsync"/>), and a group with more than a page has the rest read
    /// from its next link.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<GroupMember>>> MembersOfAsync(GraphClient graph, IEnumerable<string> groupIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<string> ids = groupIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, IReadOnlyList<GroupMember>>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return result;
        }

        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(ids.Select(id => MembersRequest(id, "members")).ToList(), cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < ids.Count; index++)
        {
            result[ids[index]] = await ReadAllAsync(graph, answers[index], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// The transitive members of each group, by its object id, each marked direct or nested and a nested one with the
    /// direct member group it comes through (task 041): the expansion of a mailbox grant held by a group (task 044).
    /// Two rounds of <c>$batch</c> whatever the number of groups (twenty requests to a call): the direct and the
    /// transitive members of every group, then the direct members of every group nested in them.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<GroupMember>>> ExpandAsync(GraphClient graph, IEnumerable<string> groupIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<string> ids = groupIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, IReadOnlyList<GroupMember>>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return result;
        }

        List<GraphRequest> requests = ids.SelectMany(id => new[] { MembersRequest(id, "members"), MembersRequest(id, "transitiveMembers") }).ToList();
        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, cancellationToken).ConfigureAwait(false);
        var direct = new Dictionary<string, IReadOnlyList<GroupMember>>(StringComparer.OrdinalIgnoreCase);
        var all = new Dictionary<string, IReadOnlyList<GroupMember>>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < ids.Count; index++)
        {
            direct[ids[index]] = await ReadAllAsync(graph, answers[2 * index], cancellationToken).ConfigureAwait(false);
            all[ids[index]] = await ReadAllAsync(graph, answers[(2 * index) + 1], cancellationToken).ConfigureAwait(false);
        }

        IEnumerable<string> nested = all.Values.SelectMany(members => members).Where(member => member.Type == "group").Select(member => member.Id)
            .Where(id => !direct.ContainsKey(id));
        var membersOf = new Dictionary<string, IReadOnlyList<GroupMember>>(direct, StringComparer.OrdinalIgnoreCase);
        foreach ((string id, IReadOnlyList<GroupMember> members) in await MembersOfAsync(graph, nested, cancellationToken).ConfigureAwait(false))
        {
            membersOf[id] = members;
        }

        foreach (string id in ids)
        {
            result[id] = Mark(direct[id], all[id], membersOf)
                .OrderBy(member => member.DisplayName ?? member.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.Id, StringComparer.Ordinal)
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// The groups a user is a member of, directly or through nesting (<c>/users/{upn}/transitiveMemberOf</c> cast to
    /// groups, an advanced query; task 044), with the names a mailbox permission entry may use for each.
    /// </summary>
    internal static async Task<IReadOnlyList<UserGroup>> MemberOfAsync(GraphClient graph, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string path = GraphQuery.Path(
            $"/users/{Uri.EscapeDataString(user)}/transitiveMemberOf/microsoft.graph.group",
            ("$count", "true"),
            ("$select", "id,displayName,mail,mailNickname"),
            ("$top", "999"));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        try
        {
            IReadOnlyList<GraphUserGroup> groups = await graph.ListAsync<GraphUserGroup>(request, cancellationToken).ConfigureAwait(false);
            return groups.Select(group => new UserGroup(group.Id, group.DisplayName ?? group.Id, group.Mail, group.MailNickname)).ToList();
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw new AegisException(FailureKind.NotFound, $"no user '{user}' in the directory", "give the user's sign-in name (user principal name)", exception);
        }
    }

    /// <summary>A batch answer's members over every page: the batch carries the first page, the next link the rest.</summary>
    private static async Task<IReadOnlyList<GroupMember>> ReadAllAsync(GraphClient graph, GraphBatchResponse answer, CancellationToken cancellationToken)
    {
        answer.EnsureSuccess();
        GraphPage<GraphDirectoryObject> page = answer.Deserialize<GraphPage<GraphDirectoryObject>>() ?? new GraphPage<GraphDirectoryObject>();
        List<GroupMember> members = page.Value.Select(item => item.ToMember()).ToList();
        if (page.NextLink is { } next)
        {
            var rest = new GraphRequest(HttpMethod.Get, next) { EventualConsistency = true, Permission = Permission };
            members.AddRange((await graph.ListAsync<GraphDirectoryObject>(rest, cancellationToken).ConfigureAwait(false)).Select(item => item.ToMember()));
        }

        return members;
    }

    /// <summary><c>group owners</c> over Graph alone: the group's owners, by name.</summary>
    public static async Task<CommandResult<GroupOwners>> OwnersAsync(GraphClient graph, string nameOrId, CancellationToken cancellationToken)
    {
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupMember> owners = await ListAsync(graph, group, "owners", cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupOwners(group, owners));
    }

    /// <summary>
    /// <c>group owners</c> over the layers of the run (task 076): the group from Graph; a distribution or mail-enabled
    /// security group that is not synced, whose owners Graph does not keep, answers from Exchange Online, its
    /// <c>ManagedBy</c> read through the sidecar the way the owner writes read it (task 064); every other group's owners
    /// come from Graph. The envelope's backend names the layers used: <c>graph+pwsh</c>, or <c>graph</c>.
    /// </summary>
    public static async Task<CommandResult<GroupOwners>> OwnersAsync(ExchangeBackends backends, string nameOrId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        GraphClient graph = backends.Graph;
        GroupInfo group = await ResolveAsync(graph, nameOrId, cancellationToken).ConfigureAwait(false);
        if (!GroupWrites.ThroughExchange(group))
        {
            return CommandResult.Success(new GroupOwners(group, await ListAsync(graph, group, "owners", cancellationToken).ConfigureAwait(false)));
        }

        IReadOnlyList<GroupMember> owners = await GroupWrites.ExchangeOwnerListAsync(backends, group, cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new GroupOwners(group, owners) { FromExchange = true });
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

    /// <summary>
    /// The request of a group's members, transitive members or owners at the largest page: <c>$select</c> on these
    /// lists is an advanced query, so <c>ConsistencyLevel</c> and <c>$count</c> go with it.
    /// </summary>
    internal static GraphRequest MembersRequest(string groupId, string relation)
    {
        string path = GraphQuery.Path(
            $"/groups/{Uri.EscapeDataString(groupId)}/{relation}",
            ("$count", "true"),
            ("$select", MemberFields),
            ("$top", "999"));
        return GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
    }

    private static async Task<IReadOnlyList<GroupMember>> ListAsync(GraphClient graph, GroupInfo group, string relation, CancellationToken cancellationToken) =>
        await ListAsync(graph, group.Id, relation, cancellationToken).ConfigureAwait(false);

    /// <summary>A group's members, transitive members or owners by its object id, by name (the writes' current state, task 052).</summary>
    internal static async Task<IReadOnlyList<GroupMember>> ListAsync(GraphClient graph, string groupId, string relation, CancellationToken cancellationToken)
    {
        IReadOnlyList<GraphDirectoryObject> objects = await graph.ListAsync<GraphDirectoryObject>(MembersRequest(groupId, relation), cancellationToken).ConfigureAwait(false);
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
