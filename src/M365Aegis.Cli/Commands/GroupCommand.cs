using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis group members|owners &lt;name or id&gt;</c> (<c>members --transitive</c>, task 033) and
/// <c>group owners-missing [--all-types]</c> (task 005):
/// the reads of the groups domain over Graph, and the writes <c>group members|owners add|remove</c> behind a plan
/// (task 052, <c>--from-csv</c> task 054). A name resolves as <see cref="GroupLookups.ResolveAsync"/> says; an
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
        Option<bool> via = new("--via")
        {
            Description = "With the nested members, name the direct member group each one comes through (a members request per nested group, batched); implies --transitive.",
        };
        Command members = new("members", "List a group's direct members, or with --transitive every member of its nested groups too.");
        members.Arguments.Add(membersOf);
        members.Options.Add(transitive);
        members.Options.Add(via);
        members.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => GroupLookups.MembersAsync(graph, parsed.GetValue(membersOf) ?? "", parsed.GetValue(transitive), parsed.GetValue(via), ct),
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

        members.Subcommands.Add(Write("add", GroupRelation.Members, add: true, json, table, profile));
        members.Subcommands.Add(Write("remove", GroupRelation.Members, add: false, json, table, profile));
        owners.Subcommands.Add(Write("add", GroupRelation.Owners, add: true, json, table, profile));
        owners.Subcommands.Add(Write("remove", GroupRelation.Owners, add: false, json, table, profile));

        group.Subcommands.Add(members);
        group.Subcommands.Add(owners);
        group.Subcommands.Add(missing);
        return group;
    }

    /// <summary>
    /// <c>group members|owners add|remove &lt;group&gt; &lt;user…&gt;</c> or <c>--from-csv &lt;file&gt;</c> (tasks 052 and 054): a
    /// plan of the users not so already, printed under <c>--dry-run</c>, applied under <c>--yes</c>.
    /// </summary>
    private static Command Write(string name, GroupRelation relation, bool add, Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        string list = relation == GroupRelation.Members ? "members" : "owners";
        Argument<string?> groupName = new("group")
        {
            Description = "The group's display name, address, object id, or a word of its name.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Argument<string[]> users = new("user")
        {
            Description = add
                ? "The users to add: user principal names, or object ids (a group, a device, a service principal too)."
                : "The users to remove: user principal names, addresses or object ids.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string?> csv = new("--from-csv")
        {
            Description = "A CSV file instead of the arguments: a header line with the columns group,user, then one change per line; one plan over every line.",
        };
        string description = add
            ? $"Add users to a group's {list}: a plan of the ones not there yet (--dry-run prints it, --yes applies it)."
            : $"Remove users from a group's {list}: a plan of the ones there (--dry-run prints it, --yes applies it).";
        Command command = new(name, description);
        command.Arguments.Add(groupName);
        command.Arguments.Add(users);
        command.Options.Add(csv);
        WriteFlags flags = WriteFlags.On(command);
        command.SetAction((parsed, cancellationToken) =>
        {
            string? file = parsed.GetValue(csv);
            string? named = parsed.GetValue(groupName);
            string[] given = parsed.GetValue(users) ?? [];
            if (!WriteRunner.TryRows(
                parsed, json, table,
                () => file is null ? GroupWrites.FromArguments(named, given) : NoArguments(named is not null || given.Length > 0, () => GroupWrites.FromCsv(file)),
                out IReadOnlyList<GroupChangeRow> rows,
                out int exitCode))
            {
                return Task.FromResult(exitCode);
            }

            return WriteRunner.GraphAsync(parsed, json, table, profile, flags, (graph, ct) => GroupWrites.PlanAsync(graph, relation, add, rows, ct), cancellationToken);
        });
        return command;
    }

    /// <summary>The rows of a CSV file, or the usage failure of arguments given beside it.</summary>
    internal static IReadOnlyList<T> NoArguments<T>(bool arguments, Func<IReadOnlyList<T>> read) => arguments
        ? throw new AegisException(FailureKind.Usage, "give the arguments or --from-csv, not both", "a CSV file names every change on its lines; nothing was sent")
        : read();

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
    private static readonly string[] ViaHeaders = ["type", "name", "sign-in name or address", "membership", "via", "id"];

    internal static GroupMembersTableView Instance { get; } = new();

    private GroupMembersTableView()
    {
    }

    public IReadOnlyList<string> Headers => GroupCommand.MemberHeaders;

    public IReadOnlyList<string> HeadersFor(GroupMembers data) => data.Via == true ? ViaHeaders : data.Transitive ? TransitiveHeaders : Headers;

    public IEnumerable<IReadOnlyList<string>> Rows(GroupMembers data)
    {
        if (data.Via == true)
        {
            return data.Members.Select(member => (IReadOnlyList<string>)
                [member.Type, member.DisplayName ?? "-", member.UserPrincipalName ?? member.Mail ?? "-", member.Nested == true ? "nested" : "direct", Via(member), member.Id]);
        }

        return data.Transitive
            ? data.Members.Select(member => (IReadOnlyList<string>)
                [member.Type, member.DisplayName ?? "-", member.UserPrincipalName ?? member.Mail ?? "-", member.Nested == true ? "nested" : "direct", member.Id])
            : data.Members.Select(GroupCommand.MemberRow);
    }

    /// <summary>The direct member groups a nested member comes through, by name; <c>-</c> for a direct member.</summary>
    private static string Via(GroupMember member) =>
        member.Via is { Count: > 0 } groups ? string.Join(", ", groups.Select(group => group.DisplayName)) : "-";

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
