using M365Aegis.Core.Groups;

namespace M365Aegis.Core.Exchange;

/// <summary>A mailbox as the lookups name it.</summary>
/// <param name="DisplayName">The display name.</param>
/// <param name="PrimarySmtpAddress">The primary address.</param>
/// <param name="Type">The recipient type details (<c>UserMailbox</c>, <c>SharedMailbox</c>, <c>RoomMailbox</c>, …).</param>
/// <param name="UserPrincipalName">The account's user principal name, or null.</param>
/// <param name="ObjectId">The Entra object id (<c>ExternalDirectoryObjectId</c>), or null.</param>
public sealed record MailboxInfo(string DisplayName, string PrimarySmtpAddress, string Type, string? UserPrincipalName, string? ObjectId);

/// <summary>The result of <c>mailbox list [--shared]</c>.</summary>
/// <param name="Mailboxes">The mailboxes, by display name.</param>
/// <param name="SharedOnly">Whether only the shared mailboxes were asked for.</param>
public sealed record MailboxList(IReadOnlyList<MailboxInfo> Mailboxes, bool SharedOnly);

/// <summary>The three ways one account can act on another's mailbox.</summary>
public enum MailboxRight
{
    /// <summary>Open the mailbox and read its content (<c>Get-EXOMailboxPermission</c>).</summary>
    FullAccess,

    /// <summary>Send mail that appears to come from the mailbox (<c>Get-EXORecipientPermission</c>).</summary>
    SendAs,

    /// <summary>Send mail on behalf of the mailbox (<c>GrantSendOnBehalfTo</c> of the mailbox).</summary>
    SendOnBehalf,
}

/// <summary>Who holds a right on a mailbox.</summary>
/// <param name="Right">The right.</param>
/// <param name="Holder">The holder as Exchange names it (a user principal name, a name, a group).</param>
/// <param name="Address">The holder's primary address, when it could be told.</param>
public sealed record MailboxHolder(MailboxRight Right, string Holder, string? Address)
{
    /// <summary>
    /// The layer that read the grant (task 063): <c>admin-api</c> for a Send on Behalf delegate the Admin API's mailbox
    /// read listed, <c>pwsh</c> for one the sidecar read (FullAccess, Send As, and every grant when the Admin API could
    /// not answer or the run has none); null from the sidecar-only lookup, which names no layer.
    /// </summary>
    public string? Layer { get; init; }

    /// <summary>
    /// Under <c>--expand-groups</c> (task 044): true when the holder is a group, false when it is not, null when the
    /// grants were not expanded.
    /// </summary>
    public bool? Group { get; init; }

    /// <summary>
    /// Under <c>--expand-groups</c>, a group holder's members over every nesting level (Graph's transitive members),
    /// each marked direct or nested and a nested one with the group it comes through (task 041); null for a holder
    /// that is not a group, or a group Graph does not hold (a dynamic distribution group).
    /// </summary>
    public IReadOnlyList<GroupMember>? Members { get; init; }
}

/// <summary>The result of <c>mailbox access &lt;mailbox&gt;</c>: the mailbox and who holds which right on it.</summary>
/// <param name="Mailbox">The mailbox.</param>
/// <param name="Holders">The holders: directly granted, allowed, the mailbox's own account left out.</param>
public sealed record MailboxAccess(MailboxInfo Mailbox, IReadOnlyList<MailboxHolder> Holders)
{
    /// <summary>
    /// The layer that read the mailbox and its Send on Behalf list (task 063): <c>admin-api</c>, or <c>pwsh</c> when the
    /// Admin API could not answer or the run has none; null from the sidecar-only lookup, which names no layer.
    /// </summary>
    public string? MailboxLayer { get; init; }
}

/// <summary>One mailbox a user holds a right on.</summary>
/// <param name="Right">The right.</param>
/// <param name="Mailbox">The mailbox; for Send As on a recipient that is not a mailbox (a group), its name only.</param>
public sealed record UserGrant(MailboxRight Right, MailboxInfo Mailbox)
{
    /// <summary>
    /// Under <c>--expand-groups</c> (task 044): the group the user holds the right through, for a grant made to a
    /// group the user is a member of (at any nesting level); null for a grant made to the user.
    /// </summary>
    public MemberGroup? Via { get; init; }
}

/// <summary>The result of <c>mailbox access --user &lt;upn&gt;</c>: every mailbox the user can open or send as.</summary>
/// <param name="User">The user as given.</param>
/// <param name="DisplayName">The user's display name, when Exchange knows the user as a recipient.</param>
/// <param name="Grants">The rights, by mailbox.</param>
/// <param name="MailboxesScanned">How many mailboxes the FullAccess and Send on Behalf lists were read from.</param>
/// <param name="Failed">The errors of the mailboxes whose permissions could not be read.</param>
public sealed record UserAccess(string User, string? DisplayName, IReadOnlyList<UserGrant> Grants, int MailboxesScanned, IReadOnlyList<string> Failed)
{
    /// <summary>
    /// How the FullAccess entries were read (task 043): <c>parallel</c> or <c>piped</c>; null from the sidecar-only
    /// overload.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>Under <c>--expand-groups</c> (task 044): how many groups the user is a member of, transitively; null otherwise.</summary>
    public int? Groups { get; init; }
}

/// <summary>How a mailbox forwards.</summary>
public enum ForwardingKind
{
    /// <summary><c>ForwardingSmtpAddress</c>: any address, set by the user in Outlook or by an administrator.</summary>
    SmtpAddress,

    /// <summary><c>ForwardingAddress</c>: a recipient of the organization (a mailbox, a mail contact, a mail user).</summary>
    Recipient,
}

/// <summary>A mailbox that forwards its mail.</summary>
/// <param name="Mailbox">The mailbox.</param>
/// <param name="Kind">Which setting forwards.</param>
/// <param name="Target">The target as Exchange stores it.</param>
/// <param name="TargetAddress">The target's address, when it could be told.</param>
/// <param name="External">Whether the address is outside the accepted domains; null when the target could not be resolved.</param>
/// <param name="KeepsCopy">Whether the mailbox keeps a copy (<c>DeliverToMailboxAndForward</c>).</param>
public sealed record Forwarder(MailboxInfo Mailbox, ForwardingKind Kind, string Target, string? TargetAddress, bool? External, bool KeepsCopy);

/// <summary>The result of <c>mailbox forwarders</c>.</summary>
/// <param name="Forwarders">The forwarding mailboxes, the external ones first.</param>
/// <param name="AcceptedDomains">The accepted domains the external flag was told against.</param>
/// <param name="MailboxesScanned">How many mailboxes were read.</param>
public sealed record MailboxForwarders(IReadOnlyList<Forwarder> Forwarders, IReadOnlyList<string> AcceptedDomains, int MailboxesScanned);

/// <summary>A mailbox's live size (<c>Get-EXOMailboxStatistics</c>).</summary>
/// <param name="Mailbox">The mailbox.</param>
/// <param name="ItemCount">The items.</param>
/// <param name="TotalItemBytes">The size of the items in bytes, or null when the service's text could not be read.</param>
/// <param name="TotalItemSize">The size as the service wrote it.</param>
/// <param name="DeletedItemCount">The items in Recoverable Items.</param>
/// <param name="DeletedItemBytes">Their size in bytes, or null.</param>
public sealed record MailboxSize(MailboxInfo Mailbox, long ItemCount, long? TotalItemBytes, string? TotalItemSize, long DeletedItemCount, long? DeletedItemBytes);

/// <summary>The result of <c>mailbox size [&lt;mailbox&gt;] [--top N]</c>.</summary>
/// <param name="Mailboxes">The sizes, the largest first.</param>
/// <param name="MailboxesScanned">How many mailboxes were read.</param>
/// <param name="Failed">The errors of the mailboxes whose size could not be read.</param>
public sealed record MailboxSizes(IReadOnlyList<MailboxSize> Mailboxes, int MailboxesScanned, IReadOnlyList<string> Failed)
{
    /// <summary>
    /// How the sizes were read (task 043): <c>piped</c> or <c>parallel</c> (live, <c>Get-EXOMailboxStatistics</c>) or
    /// <c>report</c> (Graph's mailbox usage report); null from the sidecar-only overload.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>With <c>report</c>: the day the usage report was refreshed, which its sizes are as of.</summary>
    public DateOnly? ReportDate { get; init; }

    /// <summary>With <c>report</c>: the mailboxes the report has no row for (a mailbox younger than the report).</summary>
    public int? NotInReport { get; init; }
}

/// <summary>One entry of a mailbox folder's permissions (<c>Get-MailboxFolderPermission</c>, task 042).</summary>
/// <param name="User">Who the entry is for, as Exchange names it: a display name, an address, <c>Default</c> (everyone in the organization) or <c>Anonymous</c>.</param>
/// <param name="AccessRights">The role or the granular rights (<c>Reviewer</c>, <c>Editor</c>, <c>AvailabilityOnly</c>, …).</param>
/// <param name="SharingPermissionFlags">The calendar's delegate flags (<c>Delegate</c>, <c>CanViewPrivateItems</c>, …); empty when none.</param>
public sealed record FolderPermissionEntry(string User, IReadOnlyList<string> AccessRights, IReadOnlyList<string> SharingPermissionFlags);

/// <summary>The result of <c>mailbox folder-permissions &lt;mailbox&gt; [--folder &lt;path&gt;]</c> (task 042).</summary>
/// <param name="Mailbox">The mailbox as given.</param>
/// <param name="Folder">The folder path as asked for (<c>Calendar</c>, or a nested folder such as <c>Inbox/Reports</c> written with a backslash).</param>
/// <param name="FolderName">The folder's display name as the service named it, or null.</param>
/// <param name="Permissions">The entries, in the service's order.</param>
public sealed record MailboxFolderPermissions(string Mailbox, string Folder, string? FolderName, IReadOnlyList<FolderPermissionEntry> Permissions);

/// <summary>A member of a distribution group as Exchange Online keeps it (task 042).</summary>
/// <param name="DisplayName">The display name.</param>
/// <param name="PrimarySmtpAddress">The primary address, or null.</param>
/// <param name="Type">The recipient type details (<c>UserMailbox</c>, <c>MailContact</c>, <c>MailUniversalDistributionGroup</c>, …).</param>
/// <param name="Hidden">Whether the member is hidden from the address lists.</param>
public sealed record DistributionMember(string DisplayName, string? PrimarySmtpAddress, string Type, bool Hidden);

/// <summary>The result of <c>mailbox group-members &lt;group&gt;</c> (task 042).</summary>
/// <param name="Group">The group as given.</param>
/// <param name="Members">The direct members, by name, hidden ones included.</param>
public sealed record DistributionGroupMembers(string Group, IReadOnlyList<DistributionMember> Members);

/// <summary>An accepted domain of the organization (task 042).</summary>
/// <param name="DomainName">The domain.</param>
/// <param name="DomainType">How mail for it is routed: <c>Authoritative</c>, <c>InternalRelay</c>, <c>ExternalRelay</c>; null when not given.</param>
/// <param name="Default">Whether it is the default domain; null when not given.</param>
public sealed record AcceptedDomainInfo(string DomainName, string? DomainType, bool? Default);

/// <summary>The result of <c>mailbox organization</c>: the organization's configuration the Admin API documents (task 042).</summary>
/// <param name="Name">The organization configuration's name.</param>
/// <param name="MailTipsAllTipsEnabled">Whether MailTips are on.</param>
/// <param name="MailTipsExternalRecipientsTipsEnabled">Whether the MailTip for external recipients is on.</param>
/// <param name="MailTipsLargeAudienceThreshold">The audience size from which the large audience MailTip shows.</param>
/// <param name="AcceptedDomains">The accepted domains, the default first.</param>
public sealed record ExchangeOrganization(
    string? Name,
    bool? MailTipsAllTipsEnabled,
    bool? MailTipsExternalRecipientsTipsEnabled,
    long? MailTipsLargeAudienceThreshold,
    IReadOnlyList<AcceptedDomainInfo> AcceptedDomains);

/// <summary>How <c>mailbox access --user</c> reads every mailbox's FullAccess entries (task 043).</summary>
public enum PermissionReadMethod
{
    /// <summary>The mailboxes piped into one <c>Get-EXOMailboxPermission</c>: a request per mailbox (task 016).</summary>
    Piped,

    /// <summary>
    /// The piped mailboxes split across several sidecars at once, the default (task 043: 41 s against 122 s over 620
    /// mailboxes, measured 2026-10-30). A <c>Get-EXOMailboxPermission</c> without an identity, the cmdlet's documented
    /// default set, is refused by the module ("Identity is a mandatory value"), so no listing call exists.
    /// </summary>
    Parallel,
}

/// <summary>How <c>mailbox size</c> reads every mailbox's size (task 043).</summary>
public enum MailboxSizeMethod
{
    /// <summary>The mailboxes piped into one <c>Get-EXOMailboxStatistics</c>: live, a request per mailbox (task 016).</summary>
    Piped,

    /// <summary>The piped mailboxes split across several sidecars at once: live.</summary>
    Parallel,

    /// <summary>
    /// Graph's mailbox usage report: one call, as of the report's refresh day (a few days old; three days on
    /// 2026-10-30), the default for every mailbox (task 043).
    /// </summary>
    Report,
}

/// <summary>What <c>mailbox access &lt;mailbox&gt;</c> reads beyond the plain grants.</summary>
/// <param name="ExpandGroups">Expand a grant held by a group into its members through Graph (task 044).</param>
public sealed record MailboxAccessOptions(bool ExpandGroups = false);

/// <summary>What <c>mailbox access --user</c> reads, and how.</summary>
/// <param name="ExpandGroups">Also find the grants the user holds through a group (task 044).</param>
/// <param name="Method">How the FullAccess entries are read (task 043): split across sidecars by default.</param>
/// <param name="Sidecars">How many sidecars <see cref="PermissionReadMethod.Parallel"/> runs at once (2 to 8; 8 measured no faster than 4).</param>
public sealed record UserAccessOptions(bool ExpandGroups = false, PermissionReadMethod Method = PermissionReadMethod.Parallel, int Sidecars = 4);

/// <summary>What <c>mailbox size</c> reads, and how.</summary>
/// <param name="Method">
/// How the sizes are read (task 043); null for the default: the usage report for every mailbox (16 s against 56 s
/// live over 620 mailboxes, measured 2026-10-30), the live statistics for one mailbox.
/// </param>
/// <param name="Sidecars">How many sidecars <see cref="MailboxSizeMethod.Parallel"/> runs at once (2 to 8).</param>
public sealed record MailboxSizeOptions(MailboxSizeMethod? Method = null, int Sidecars = 4);
