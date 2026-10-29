using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>
/// The allow-list of the Exchange Online sidecar (D-004, task 015): the one place a cmdlet is admitted. The session
/// refuses any other name before anything is sent; the script carries the same list as its own guard, and a test
/// holds the two equal (the script is hashed into the build, so the copy cannot drift after it). A cmdlet joins
/// the list with the lookup that needs it, and leaves it with the last one.
/// </summary>
public static class ExoCmdlets
{
    /// <summary>The mailboxes, with their properties (the module's own bulk reader).</summary>
    public const string GetMailbox = "Get-EXOMailbox";

    /// <summary>FullAccess and the other mailbox permissions.</summary>
    public const string GetMailboxPermission = "Get-EXOMailboxPermission";

    /// <summary>Send As.</summary>
    public const string GetRecipientPermission = "Get-EXORecipientPermission";

    /// <summary>The live mailbox sizes and item counts.</summary>
    public const string GetMailboxStatistics = "Get-EXOMailboxStatistics";

    /// <summary>Any recipient: a forwarding target, a Send on Behalf holder, the user of a reverse lookup.</summary>
    public const string GetRecipient = "Get-EXORecipient";

    /// <summary>The organization's accepted domains, which tell an external forwarding address.</summary>
    public const string GetAcceptedDomain = "Get-AcceptedDomain";

    /// <summary>
    /// A mailbox's folders of one kind: the default calendar's name in the mailbox's own language (task 042), which
    /// <c>Get-MailboxFolderPermission</c> needs in its folder path.
    /// </summary>
    public const string GetMailboxFolderStatistics = "Get-EXOMailboxFolderStatistics";

    /// <summary>Every allowed cmdlet, in the order the script lists them.</summary>
    public static IReadOnlyList<string> Allowed { get; } =
        [GetMailbox, GetMailboxPermission, GetRecipientPermission, GetMailboxStatistics, GetRecipient, GetAcceptedDomain, GetMailboxFolderStatistics];

    /// <summary>Whether the cmdlet is on the list (PowerShell names compare without case).</summary>
    public static bool IsAllowed(string? cmdlet) =>
        cmdlet is not null && Allowed.Contains(cmdlet, StringComparer.OrdinalIgnoreCase);

    /// <summary>Throws the <see cref="FailureKind.CmdletNotAllowed"/> failure when the cmdlet is not on the list.</summary>
    public static void EnsureAllowed(string? cmdlet)
    {
        if (!IsAllowed(cmdlet))
        {
            throw new AegisException(
                FailureKind.CmdletNotAllowed,
                $"'{cmdlet}' is not on the Exchange Online sidecar's allow-list; nothing ran",
                $"the sidecar runs {string.Join(", ", Allowed)} only (D-004)");
        }
    }
}
