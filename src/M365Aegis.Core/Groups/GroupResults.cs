using System.Text.Json.Serialization;
using M365Aegis.Core.Graph;

namespace M365Aegis.Core.Groups;

/// <summary>What kind of group it is, from its type, mail and security flags.</summary>
public enum GroupKind
{
    /// <summary>A Microsoft 365 group (<c>groupTypes</c> holds <c>Unified</c>): a team, a site, a mailbox.</summary>
    Microsoft365,

    /// <summary>A security group without mail.</summary>
    Security,

    /// <summary>A mail-enabled security group, managed in Exchange.</summary>
    MailEnabledSecurity,

    /// <summary>A distribution group, managed in Exchange.</summary>
    Distribution,
}

/// <summary>One group as the lookups show it.</summary>
/// <param name="Id">The object id.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Kind">The kind of group.</param>
/// <param name="Mail">The primary address, or null for a security group.</param>
/// <param name="Synced">True when the group comes from on-premises Active Directory.</param>
public sealed record GroupInfo(string Id, string DisplayName, GroupKind Kind, string? Mail, bool Synced)
{
    /// <summary>
    /// True when Graph keeps the owners of this kind of group: a Microsoft 365 group or a security group made in the
    /// cloud. Distribution and mail-enabled security groups are owned in Exchange, synced ones on-premises.
    /// </summary>
    [JsonIgnore]
    public bool OwnersInGraph => !Synced && Kind is GroupKind.Microsoft365 or GroupKind.Security;
}

/// <summary>A member or an owner of a group.</summary>
/// <param name="Type">The object's type: <c>user</c>, <c>group</c>, <c>device</c>, <c>servicePrincipal</c>, …</param>
/// <param name="DisplayName">The display name, or null.</param>
/// <param name="UserPrincipalName">A user's sign-in name, or null.</param>
/// <param name="Mail">The address, or null.</param>
/// <param name="Id">The object id.</param>
/// <param name="Nested">
/// In a transitive list (<c>group members --transitive</c>), true for a member through a nested group and false for a
/// direct one; null in a direct list.
/// </param>
public sealed record GroupMember(string Type, string? DisplayName, string? UserPrincipalName, string? Mail, string Id, bool? Nested = null)
{
    /// <summary>
    /// In a transitive list asked for with the via groups (<c>group members --via</c>, task 041): for a nested member,
    /// the direct member group (or groups) of the listed group it is reached through, by name; null otherwise.
    /// </summary>
    public IReadOnlyList<MemberGroup>? Via { get; init; }
}

/// <summary>A group a nested member is reached through.</summary>
/// <param name="Id">The group's object id.</param>
/// <param name="DisplayName">The group's display name, or its id when Graph gave none.</param>
public sealed record MemberGroup(string Id, string DisplayName);

/// <summary>The result of <c>group members</c>.</summary>
/// <param name="Group">The group the name resolved to.</param>
/// <param name="Members">The members, by name: the direct ones, or with <paramref name="Transitive"/> every member of the nested groups too.</param>
/// <param name="Transitive">Whether the list is transitive (<c>--transitive</c>), each member marked direct or nested.</param>
public sealed record GroupMembers(GroupInfo Group, IReadOnlyList<GroupMember> Members, bool Transitive = false)
{
    /// <summary>
    /// Whether each nested member names the direct member group it came through (<see cref="GroupMember.Via"/>,
    /// <c>--via</c>, task 041): one <c>members</c> request per group of the transitive list, batched. True when asked
    /// for, null otherwise, so the envelope of a list without it is unchanged.
    /// </summary>
    public bool? Via { get; init; }
}

/// <summary>The result of <c>group owners</c>.</summary>
/// <param name="Group">The group the name resolved to.</param>
/// <param name="Owners">The owners, by name; empty when there is none.</param>
public sealed record GroupOwners(GroupInfo Group, IReadOnlyList<GroupMember> Owners)
{
    /// <summary>
    /// True when the owners are the group's <c>ManagedBy</c> in Exchange Online (a distribution or mail-enabled
    /// security group, task 076), each resolved to its recipient or, when none answers to the name (a role group), as
    /// <c>ManagedBy</c> names it; null when Graph answered, so the envelope of a Graph list is unchanged.
    /// </summary>
    public bool? FromExchange { get; init; }
}

/// <summary>The result of <c>group owners-missing</c>.</summary>
/// <param name="Groups">The groups without an owner, by name.</param>
/// <param name="LeftOut">
/// The groups without an owner in Graph that were left out because Graph keeps no owners for them (distribution,
/// mail-enabled security and synced groups); 0 under <c>--all-types</c>.
/// </param>
public sealed record OwnerlessGroups(IReadOnlyList<GroupInfo> Groups, int LeftOut)
{
    /// <summary>
    /// Under <c>--all-types</c> over the Exchange layers (task 089): how many of the distribution and mail-enabled
    /// security groups Graph lists without an owner were left out because their <c>ManagedBy</c> in Exchange Online
    /// names one; null when Exchange was not asked (the default list, or Graph alone), so that envelope is unchanged.
    /// </summary>
    public int? ManagedInExchange { get; init; }

    /// <summary>
    /// What limits the answer (task 096): under <c>--all-types</c>, Exchange Online refused the <c>ManagedBy</c> read, so
    /// the distribution and mail-enabled security groups are judged by Graph's empty owner list; null when nothing does,
    /// so every other envelope is unchanged.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }
}

/// <summary>One team of <c>team list</c>.</summary>
/// <param name="Id">The team's id, which is its Microsoft 365 group's id.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Visibility">public, private, or null.</param>
/// <param name="Description">The description, or null.</param>
public sealed record TeamInfo(string Id, string DisplayName, string? Visibility, string? Description);

/// <summary>One site of <c>site list</c>.</summary>
/// <param name="Id">The site id (<c>host,site collection id,web id</c>).</param>
/// <param name="DisplayName">The display name, or the URL name when there is none.</param>
/// <param name="WebUrl">The site's address.</param>
/// <param name="LastModified">When the site last changed, or null.</param>
public sealed record SiteInfo(string Id, string DisplayName, string? WebUrl, DateTimeOffset? LastModified);

/// <summary>A group as Graph returns it to the lookups' <c>$select</c>.</summary>
internal sealed record GraphGroup : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? Mail { get; init; }

    public IReadOnlyList<string>? GroupTypes { get; init; }

    public bool? SecurityEnabled { get; init; }

    public bool? OnPremisesSyncEnabled { get; init; }

    /// <summary>The kind: Unified first; a group with an address is mail-enabled.</summary>
    public GroupInfo ToInfo()
    {
        bool unified = GroupTypes?.Contains("Unified", StringComparer.OrdinalIgnoreCase) == true;
        bool mail = !string.IsNullOrEmpty(Mail);
        GroupKind kind = unified ? GroupKind.Microsoft365
            : SecurityEnabled == true ? (mail ? GroupKind.MailEnabledSecurity : GroupKind.Security)
            : GroupKind.Distribution;
        return new GroupInfo(Id, DisplayName ?? Id, kind, Mail, OnPremisesSyncEnabled == true);
    }
}

/// <summary>A member or owner as Graph returns it: any directory object.</summary>
internal sealed record GraphDirectoryObject : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }

    public GroupMember ToMember()
    {
        string type = ODataType is { } odata && odata.StartsWith("#microsoft.graph.", StringComparison.Ordinal)
            ? odata["#microsoft.graph.".Length..]
            : "directoryObject";
        return new GroupMember(type, DisplayName, UserPrincipalName, Mail, Id);
    }
}

/// <summary>A team as <c>GET /teams</c> returns it.</summary>
internal sealed record GraphTeam : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string? Visibility { get; init; }
}

/// <summary>A site as the site search returns it.</summary>
internal sealed record GraphSite : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? Name { get; init; }

    public string? WebUrl { get; init; }

    public DateTimeOffset? LastModifiedDateTime { get; init; }
}

/// <summary>A group a user is a member of, with the names a mailbox permission entry may use for it (task 044).</summary>
/// <param name="Id">The group's object id.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Mail">The primary address, or null.</param>
/// <param name="MailNickname">The alias, or null.</param>
internal sealed record UserGroup(string Id, string DisplayName, string? Mail, string? MailNickname);

/// <summary>A group as <c>transitiveMemberOf</c> returns it to the lookup's <c>$select</c>.</summary>
internal sealed record GraphUserGroup : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? Mail { get; init; }

    public string? MailNickname { get; init; }
}
