namespace M365Aegis.Core.Auth;

/// <summary>A Microsoft Entra built-in role, by the name the setup guide prints and its role template id.</summary>
/// <param name="Name">The role's name in the Microsoft Entra admin center (<c>Reports Reader</c>).</param>
/// <param name="TemplateId">The role template id, the same in every tenant (the Entra built-in roles reference).</param>
public sealed record EntraRole(string Name, Guid TemplateId);

/// <summary>How the doctor can judge a row of the setup guide's role column from the account's Entra roles.</summary>
public enum RoleKind
{
    /// <summary>The row needs one of the Entra roles: a missing one is a gap.</summary>
    Entra,

    /// <summary>The row needs one of the Entra roles, or the account owns the group: a missing role may not be a gap.</summary>
    EntraOrOwner,

    /// <summary>
    /// The row needs an Exchange Online role group, which Microsoft Entra does not show: the Entra roles that carry the
    /// group count, and without one the row is noted, never warned.
    /// </summary>
    Exchange,
}

/// <summary>One row of the setup guide's permissions table that names a role, and the Entra roles that answer it.</summary>
/// <param name="Section">The table the row is in (the heading above it).</param>
/// <param name="Commands">The row's first column as the guide prints it, without the backticks.</param>
/// <param name="AnyOf">The Entra roles any one of which is enough, the ones the row names first.</param>
/// <param name="Kind">How a missing role is reported.</param>
public sealed record RoleRow(string Section, string Commands, IReadOnlyList<EntraRole> AnyOf, RoleKind Kind);

/// <summary>
/// The role column of the setup guide's permissions table for the doctor's <c>roles</c> row (task 111): every row of
/// <c>docs/setup.md</c>'s tables whose role column names a role, in the guide's order, with the Microsoft Entra roles
/// that answer it. The guide stays the one list an administrator reads; <c>RoleMapTests</c> holds this map to it.
/// Global Administrator answers every row. An Exchange Online role group (Organization Management, Recipient
/// Management, View-Only Organization Management) is not an Entra role: Exchange Administrator and Global
/// Administrator carry Organization Management and Global Reader carries View-Only Organization Management, so those
/// count, and a row that none of them answers is noted, never warned, since a direct role group membership is invisible
/// to Microsoft Entra.
/// </summary>
public static class RoleMap
{
    /// <summary>Can manage all aspects of Microsoft Entra ID and the services that use Microsoft Entra identities.</summary>
    public static EntraRole GlobalAdministrator { get; } = new("Global Administrator", new("62e90394-69f5-4237-9190-012177145e10"));

    /// <summary>Global permissions within Exchange Online (Organization Management there).</summary>
    public static EntraRole ExchangeAdministrator { get; } = new("Exchange Administrator", new("29232cdf-9323-42fd-ade2-1d097af3e4de"));

    /// <summary>Reads what a Global Administrator can (View-Only Organization Management in Exchange Online).</summary>
    public static EntraRole GlobalReader { get; } = new("Global Reader", new("f2ef992c-3afb-46b9-b7cf-a126ee74c451"));

    /// <summary>Reads the sign-in and audit logs and the usage reports.</summary>
    public static EntraRole ReportsReader { get; } = new("Reports Reader", new("4a5d8f65-41da-4de4-8968-e035b65339cf"));

    /// <summary>Reads security information and reports, the sign-in and audit logs among them.</summary>
    public static EntraRole SecurityReader { get; } = new("Security Reader", new("5d6b6bb7-de71-4623-b4af-96380a352509"));

    /// <summary>Creates and manages groups.</summary>
    public static EntraRole GroupsAdministrator { get; } = new("Groups Administrator", new("fdd7a751-b60b-444a-984c-02652fe8fa1c"));

    /// <summary>Manages users and groups.</summary>
    public static EntraRole UserAdministrator { get; } = new("User Administrator", new("fe930be7-5e62-47db-91af-98c3a49a38b1"));

    /// <summary>Every role the map knows, by name in the doctor's detail.</summary>
    public static IReadOnlyList<EntraRole> Roles { get; } =
        [GlobalAdministrator, ExchangeAdministrator, GlobalReader, ReportsReader, SecurityReader, GroupsAdministrator, UserAdministrator];

    private const string Lookups = "Read-only: the lookups";
    private const string Writes = "Writes: every change behind a plan";
    private const string Snapshots = "Snapshots";

    /// <summary>The roles that carry an Exchange Online role group that only reads (View-Only Organization Management).</summary>
    private static readonly EntraRole[] ExchangeRead = [ExchangeAdministrator, GlobalAdministrator, GlobalReader];

    /// <summary>The roles that carry Organization Management in Exchange Online.</summary>
    private static readonly EntraRole[] ExchangeManage = [ExchangeAdministrator, GlobalAdministrator];

    /// <summary>The table's rows that name a role, in the guide's order.</summary>
    public static IReadOnlyList<RoleRow> Rows { get; } =
    [
        new(Lookups, "group members, group owners, group owners-missing", ExchangeRead, RoleKind.Exchange),
        new(Lookups, "signin list, audit list", [ReportsReader, SecurityReader, GlobalReader, GlobalAdministrator], RoleKind.Entra),
        // The Message Tracking role: Organization Management carries it, View-Only Organization Management does not.
        new(Lookups, "trace, trace details", ExchangeManage, RoleKind.Exchange),
        new(Lookups,
            "mailbox list, mailbox access, mailbox forwarders, mailbox folder-permissions, mailbox group-members, mailbox organization, mailbox size <mailbox>, the window's mailbox cards",
            ExchangeRead, RoleKind.Exchange),
        new(Lookups, "mailbox size over every mailbox", [ReportsReader, GlobalReader, GlobalAdministrator], RoleKind.Entra),
        new(Writes, "group members add, group members remove (Microsoft 365 and security groups)",
            [GroupsAdministrator, UserAdministrator, GlobalAdministrator], RoleKind.EntraOrOwner),
        new(Writes, "group owners add, group owners remove (Microsoft 365 and security groups)",
            [GroupsAdministrator, UserAdministrator, GlobalAdministrator], RoleKind.EntraOrOwner),
        new(Writes, "the same four writes on a distribution group or a mail-enabled security group", ExchangeManage, RoleKind.Exchange),
        new(Writes,
            "mailbox access grant, mailbox access revoke, mailbox forwarding set, mailbox forwarding clear, the window's Mailbox rights and Mailbox forwarding cards",
            ExchangeManage, RoleKind.Exchange),
        new(Snapshots, "snapshot take (the mailboxes, their rights, calendars and forwarding, the distribution lists' owners)", ExchangeRead, RoleKind.Exchange),
    ];
}
