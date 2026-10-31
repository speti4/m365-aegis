namespace M365Aegis.Core.Auth;

/// <summary>One delegated permission a command needs, and the API whose token carries it.</summary>
/// <param name="Resource">The API: Microsoft Graph or Exchange Online.</param>
/// <param name="Name">The permission as a token lists it (<c>GroupMember.Read.All</c>), without the resource prefix.</param>
public sealed record NeededScope(Resource Resource, string Name);

/// <summary>One row of the setup guide's permissions table: the command group and the permissions it needs.</summary>
/// <param name="Section">The table the row is in (the heading above it, <c>Read-only: the lookups</c>).</param>
/// <param name="Commands">The row's first column as the guide prints it, without the backticks.</param>
/// <param name="Permissions">The delegated permissions the row names, in the order it names them.</param>
public sealed record PermissionRow(string Section, string Commands, IReadOnlyList<NeededScope> Permissions);

/// <summary>
/// The command-to-permission map of the doctor's <c>consent</c> row (task 109): every row of the permissions table of
/// <c>docs/setup.md</c> that names a delegated permission, in the guide's order. The guide stays the one list an
/// administrator reads; the test <c>PermissionMapTests</c> reads the table and holds this map to it row by row, and to
/// the guide's Azure CLI lists per API, so the two cannot drift. The rows that name no permission (<c>call</c>, whose
/// permission is the endpoint's, and the snapshot reads, which stay on the machine) are not here. The role column is
/// not either: no token carries the account's Exchange Online roles, so the doctor checks the consent, not the roles.
/// </summary>
public static class PermissionMap
{
    private const string Lookups = "Read-only: the lookups";
    private const string Writes = "Writes: every change behind a plan";
    private const string Snapshots = "Snapshots";

    /// <summary>The table's rows that name a permission, in the guide's order.</summary>
    public static IReadOnlyList<PermissionRow> Rows { get; } =
    [
        new(Lookups, "login, whoami, the window's sign-in", [Graph("User.Read")]),
        new(Lookups, "group members, group owners, group owners-missing",
            [Graph("GroupMember.Read.All"), Graph("User.ReadBasic.All"), Exchange("Exchange.Manage")]),
        new(Lookups, "team list", [Graph("Team.ReadBasic.All")]),
        new(Lookups, "site list", [Graph("Sites.Read.All")]),
        new(Lookups, "signin list, audit list", [Graph("AuditLog.Read.All")]),
        new(Lookups, "secrets expiring", [Graph("Application.Read.All")]),
        new(Lookups, "trace, trace details", [Graph("ExchangeMessageTrace.Read.All")]),
        new(Lookups,
            "mailbox list, mailbox access, mailbox forwarders, mailbox folder-permissions, mailbox group-members, mailbox organization, mailbox size <mailbox>, the window's mailbox cards",
            [Exchange("Exchange.Manage"), Exchange("Exchange.ManageV2"), Graph("GroupMember.Read.All")]),
        new(Lookups, "mailbox size over every mailbox", [Graph("Reports.Read.All")]),
        new(Writes, "group members add, group members remove (Microsoft 365 and security groups)",
            [Graph("GroupMember.ReadWrite.All"), Graph("User.ReadBasic.All")]),
        new(Writes, "group owners add, group owners remove (Microsoft 365 and security groups)",
            [Graph("Group.ReadWrite.All"), Graph("User.ReadBasic.All")]),
        new(Writes, "the same four writes on a distribution group or a mail-enabled security group",
            [Exchange("Exchange.Manage"), Exchange("Exchange.ManageV2")]),
        new(Writes,
            "mailbox access grant, mailbox access revoke, mailbox forwarding set, mailbox forwarding clear, the window's Mailbox rights and Mailbox forwarding cards",
            [Exchange("Exchange.Manage"), Exchange("Exchange.ManageV2")]),
        new(Snapshots, "snapshot take",
            [Graph("User.ReadBasic.All"), Graph("GroupMember.Read.All"), Graph("Team.ReadBasic.All"), Graph("Sites.Read.All")]),
        new(Snapshots, "snapshot take (the mailboxes, their rights, calendars and forwarding, the distribution lists' owners)",
            [Exchange("Exchange.Manage"), Exchange("Exchange.ManageV2")]),
    ];

    /// <summary>Every permission of the map once, in the order the rows first name it.</summary>
    public static IReadOnlyList<NeededScope> Permissions { get; } = [.. Rows.SelectMany(row => row.Permissions).Distinct()];

    /// <summary>The API's name as the guide and the portal print it.</summary>
    public static string ApiName(Resource resource) => resource switch
    {
        Resource.Graph => "Microsoft Graph",
        Resource.Exchange => "Exchange Online",
        _ => "?",
    };

    private static NeededScope Graph(string name) => new(Resource.Graph, name);

    private static NeededScope Exchange(string name) => new(Resource.Exchange, name);
}
