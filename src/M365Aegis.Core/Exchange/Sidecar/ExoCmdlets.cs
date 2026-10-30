using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>
/// The allow-list of the Exchange Online sidecar (D-004, task 015): the one place a cmdlet is admitted. The session
/// refuses any other name before anything is sent; the script carries the same list as its own guard, and a test
/// holds the two equal (the script is hashed into the build, so the copy cannot drift after it). A cmdlet joins
/// the list with the lookup that needs it, and leaves it with the last one. The write cmdlets (task 053) run with the
/// parameters of <see cref="WriteParameters"/> only, which the script holds as its own guard too: <c>Set-Mailbox</c>
/// can change anything on a mailbox, and the list keeps it to what the plans send.
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

    /// <summary>Grants FullAccess (task 053).</summary>
    public const string AddMailboxPermission = "Add-MailboxPermission";

    /// <summary>Revokes FullAccess (task 053).</summary>
    public const string RemoveMailboxPermission = "Remove-MailboxPermission";

    /// <summary>Grants Send As (task 053).</summary>
    public const string AddRecipientPermission = "Add-RecipientPermission";

    /// <summary>Revokes Send As (task 053).</summary>
    public const string RemoveRecipientPermission = "Remove-RecipientPermission";

    /// <summary>Send on Behalf and the forwarding (task 053), with the parameters of <see cref="WriteParameters"/> only.</summary>
    public const string SetMailbox = "Set-Mailbox";

    /// <summary>Adds a member to a distribution or mail-enabled security group (task 061).</summary>
    public const string AddDistributionGroupMember = "Add-DistributionGroupMember";

    /// <summary>Removes a member from a distribution or mail-enabled security group (task 061).</summary>
    public const string RemoveDistributionGroupMember = "Remove-DistributionGroupMember";

    /// <summary>Every allowed cmdlet, in the order the script lists them.</summary>
    public static IReadOnlyList<string> Allowed { get; } =
    [
        GetMailbox, GetMailboxPermission, GetRecipientPermission, GetMailboxStatistics, GetRecipient, GetAcceptedDomain, GetMailboxFolderStatistics,
        AddMailboxPermission, RemoveMailboxPermission, AddRecipientPermission, RemoveRecipientPermission, SetMailbox,

        // The member writes of a distribution or mail-enabled security group (task 061).
        AddDistributionGroupMember, RemoveDistributionGroupMember,
    ];

    /// <summary>
    /// The parameters each write cmdlet may be given, in the order the script lists them (task 053); a read cmdlet takes
    /// any of its parameters. <c>Confirm</c> is <c>$false</c> on every write, since the sidecar cannot answer a prompt.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> WriteParameters { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [AddMailboxPermission] = ["Identity", "User", "AccessRights", "InheritanceType", "AutoMapping", "Confirm"],
        [RemoveMailboxPermission] = ["Identity", "User", "AccessRights", "InheritanceType", "Confirm"],
        [AddRecipientPermission] = ["Identity", "Trustee", "AccessRights", "Confirm"],
        [RemoveRecipientPermission] = ["Identity", "Trustee", "AccessRights", "Confirm"],
        [SetMailbox] = ["Identity", "GrantSendOnBehalfTo", "ForwardingSmtpAddress", "ForwardingAddress", "DeliverToMailboxAndForward", "Confirm"],

        // The member writes of a distribution or mail-enabled security group (task 061).
        [AddDistributionGroupMember] = ["Identity", "Member", "BypassSecurityGroupManagerCheck", "Confirm"],
        [RemoveDistributionGroupMember] = ["Identity", "Member", "BypassSecurityGroupManagerCheck", "Confirm"],
    };

    /// <summary>Whether the cmdlet is on the list (PowerShell names compare without case).</summary>
    public static bool IsAllowed(string? cmdlet) =>
        cmdlet is not null && Allowed.Contains(cmdlet, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Throws the <see cref="FailureKind.CmdletNotAllowed"/> failure when the cmdlet is not on the list, or when a write
    /// cmdlet is given a parameter off its <see cref="WriteParameters"/>.
    /// </summary>
    public static void EnsureAllowed(ExoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureAllowed(command.Cmdlet);
        if (WriteParameters.TryGetValue(command.Cmdlet, out IReadOnlyList<string>? allowed))
        {
            string? off = command.Parameters.Keys.FirstOrDefault(name => !allowed.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (off is not null)
            {
                throw new AegisException(
                    FailureKind.CmdletNotAllowed,
                    $"'{command.Cmdlet} -{off}' is not on the Exchange Online sidecar's allow-list; nothing ran",
                    $"{command.Cmdlet} runs with {string.Join(", ", allowed)} only (D-004)");
            }
        }
    }

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
