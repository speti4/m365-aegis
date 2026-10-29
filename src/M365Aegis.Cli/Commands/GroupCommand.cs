using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Groups;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis group members|owners &lt;name or id&gt;</c> (<c>members --transitive</c>, task 033) and
/// <c>group owners-missing [--all-types]</c> (task 005):
/// the reads of the groups domain over Graph. A name resolves as <see cref="GroupLookups.ResolveAsync"/> says; an
/// ambiguous one exits 4 with the candidates in the hint.
/// </summary>
internal static class GroupCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command group = new("group", "Look up groups: members, owners, groups without an owner.");

        Argument<string> membersOf = new("group") { Description = "The group's display name, address, object id, or a word of its name." };
        Option<bool> transitive = new("--transitive")
        {
            Description = "Also list the members of the nested groups, each marked direct or nested.",
        };
        Command members = new("members", "List a group's direct members, or with --transitive every member of its nested groups too.");
        members.Arguments.Add(membersOf);
        members.Options.Add(transitive);
        members.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => GroupLookups.MembersAsync(graph, parsed.GetValue(membersOf) ?? "", parsed.GetValue(transitive), ct),
            GroupMembersTableView.Instance,
            cancellationToken));

        Argument<string> ownersOf = new("group") { Description = "The group's display name, address, object id, or a word of its name." };
        Command owners = new("owners", "List a group's owners.");
        owners.Arguments.Add(ownersOf);
        owners.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => GroupLookups.OwnersAsync(graph, parsed.GetValue(ownersOf) ?? "", ct),
            GroupOwnersTableView.Instance,
            cancellationToken));

        Option<bool> allTypes = new("--all-types")
        {
            Description = "Also list the distribution, mail-enabled security and synced groups, whose owners Graph does not keep.",
        };
        Command missing = new("owners-missing", "List every group without an owner.");
        missing.Options.Add(allTypes);
        missing.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => GroupLookups.OwnerlessAsync(graph, parsed.GetValue(allTypes), ct),
            OwnerlessGroupsTableView.Instance,
            cancellationToken));

        group.Subcommands.Add(members);
        group.Subcommands.Add(owners);
        group.Subcommands.Add(missing);
        return group;
    }

    /// <summary>A group kind as the tables write it.</summary>
    internal static string Kind(GroupKind kind) => kind switch
    {
        GroupKind.Microsoft365 => "Microsoft 365",
        GroupKind.Security => "security",
        GroupKind.MailEnabledSecurity => "mail-enabled security",
        GroupKind.Distribution => "distribution",
        _ => "?",
    };

    /// <summary>The columns of a member or an owner.</summary>
    internal static readonly string[] MemberHeaders = ["type", "name", "sign-in name or address", "id"];

    /// <summary>A member or an owner as a row.</summary>
    internal static IReadOnlyList<string> MemberRow(GroupMember member) =>
        [member.Type, member.DisplayName ?? "-", member.UserPrincipalName ?? member.Mail ?? "-", member.Id];

    /// <summary>The group a list belongs to, as the line under the table.</summary>
    internal static string Describe(GroupInfo group) =>
        $"{group.DisplayName} ({Kind(group.Kind)}{(group.Synced ? ", synced" : "")}, {group.Id})";
}

/// <summary>
/// The table of <c>group members</c>: a row per member, the group under the table; a transitive list has a
/// membership column (direct or nested) and counts the nested members under the table.
/// </summary>
internal sealed class GroupMembersTableView : ITableView<GroupMembers>
{
    private static readonly string[] TransitiveHeaders = ["type", "name", "sign-in name or address", "membership", "id"];

    internal static GroupMembersTableView Instance { get; } = new();

    private GroupMembersTableView()
    {
    }

    public IReadOnlyList<string> Headers => GroupCommand.MemberHeaders;

    public IReadOnlyList<string> HeadersFor(GroupMembers data) => data.Transitive ? TransitiveHeaders : Headers;

    public IEnumerable<IReadOnlyList<string>> Rows(GroupMembers data) => data.Transitive
        ? data.Members.Select(member => (IReadOnlyList<string>)
            [member.Type, member.DisplayName ?? "-", member.UserPrincipalName ?? member.Mail ?? "-", member.Nested == true ? "nested" : "direct", member.Id])
        : data.Members.Select(GroupCommand.MemberRow);

    public IEnumerable<string> Footer(GroupMembers data)
    {
        if (!data.Transitive)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} direct member(s) of {1}", data.Members.Count, GroupCommand.Describe(data.Group));
            yield break;
        }

        int nested = data.Members.Count(member => member.Nested == true);
        int groups = data.Members.Count(member => member.Type == "group");
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} member(s) of {1}: {2} direct, {3} through the {4} nested group(s) among them",
            data.Members.Count,
            GroupCommand.Describe(data.Group),
            data.Members.Count - nested,
            nested,
            groups);
    }
}

/// <summary>The table of <c>group owners</c>: a row per owner, the group under the table.</summary>
internal sealed class GroupOwnersTableView : ITableView<GroupOwners>
{
    internal static GroupOwnersTableView Instance { get; } = new();

    private GroupOwnersTableView()
    {
    }

    public IReadOnlyList<string> Headers => GroupCommand.MemberHeaders;

    public IEnumerable<IReadOnlyList<string>> Rows(GroupOwners data) => data.Owners.Select(GroupCommand.MemberRow);

    public IEnumerable<string> Footer(GroupOwners data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} owner(s) of {1}", data.Owners.Count, GroupCommand.Describe(data.Group));
        if (!data.Group.OwnersInGraph)
        {
            yield return "Graph keeps no owners for this kind of group: Exchange or on-premises Active Directory manages it";
        }
    }
}

/// <summary>The table of <c>group owners-missing</c>: a row per group, what was left out under the table.</summary>
internal sealed class OwnerlessGroupsTableView : ITableView<OwnerlessGroups>
{
    internal static OwnerlessGroupsTableView Instance { get; } = new();

    private OwnerlessGroupsTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["name", "type", "address", "id"];

    public IEnumerable<IReadOnlyList<string>> Rows(OwnerlessGroups data) => data.Groups.Select(group => (IReadOnlyList<string>)
        [group.DisplayName, GroupCommand.Kind(group.Kind) + (group.Synced ? ", synced" : ""), group.Mail ?? "-", group.Id]);

    public IEnumerable<string> Footer(OwnerlessGroups data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} group(s) without an owner", data.Groups.Count);
        if (data.LeftOut > 0)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "{0} distribution, mail-enabled security or synced group(s) left out: Graph keeps no owners for them (--all-types lists them)",
                data.LeftOut);
        }
    }
}
