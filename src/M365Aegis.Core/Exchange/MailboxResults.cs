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
public sealed record MailboxHolder(MailboxRight Right, string Holder, string? Address);

/// <summary>The result of <c>mailbox access &lt;mailbox&gt;</c>: the mailbox and who holds which right on it.</summary>
/// <param name="Mailbox">The mailbox.</param>
/// <param name="Holders">The holders: directly granted, allowed, the mailbox's own account left out.</param>
public sealed record MailboxAccess(MailboxInfo Mailbox, IReadOnlyList<MailboxHolder> Holders);

/// <summary>One mailbox a user holds a right on.</summary>
/// <param name="Right">The right.</param>
/// <param name="Mailbox">The mailbox; for Send As on a recipient that is not a mailbox (a group), its name only.</param>
public sealed record UserGrant(MailboxRight Right, MailboxInfo Mailbox);

/// <summary>The result of <c>mailbox access --user &lt;upn&gt;</c>: every mailbox the user can open or send as.</summary>
/// <param name="User">The user as given.</param>
/// <param name="DisplayName">The user's display name, when Exchange knows the user as a recipient.</param>
/// <param name="Grants">The rights, by mailbox.</param>
/// <param name="MailboxesScanned">How many mailboxes the FullAccess and Send on Behalf lists were read from.</param>
/// <param name="Failed">The errors of the mailboxes whose permissions could not be read.</param>
public sealed record UserAccess(string User, string? DisplayName, IReadOnlyList<UserGrant> Grants, int MailboxesScanned, IReadOnlyList<string> Failed);

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
public sealed record MailboxSizes(IReadOnlyList<MailboxSize> Mailboxes, int MailboxesScanned, IReadOnlyList<string> Failed);
