using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.AdminApi;

/// <summary>
/// The cmdlets the Exchange Online Admin API backend runs (D-004's second layer, task 042), each with the documented
/// endpoint that takes it (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-reference): the reads
/// only, since a write goes through a plan (M3). The undocumented <c>adminapi/beta/InvokeCommand</c> endpoint, which
/// would run any cmdlet, is never called; a cmdlet that is not here is refused before anything is sent, as the
/// sidecar refuses one off its own list (exit code 12).
/// </summary>
public static class AdminApiCmdlets
{
    /// <summary>A mailbox or every mailbox, with the Send on Behalf delegates (the <c>Mailbox</c> endpoint).</summary>
    public const string GetMailbox = "Get-Mailbox";

    /// <summary>The permissions on one folder of a mailbox (the <c>MailboxFolderPermission</c> endpoint).</summary>
    public const string GetMailboxFolderPermission = "Get-MailboxFolderPermission";

    /// <summary>The members of a distribution group (the <c>DistributionGroupMember</c> endpoint).</summary>
    public const string GetDistributionGroupMember = "Get-DistributionGroupMember";

    /// <summary>The organization's configuration (the <c>OrganizationConfig</c> endpoint).</summary>
    public const string GetOrganizationConfig = "Get-OrganizationConfig";

    /// <summary>The organization's accepted domains (the <c>AcceptedDomain</c> endpoint).</summary>
    public const string GetAcceptedDomain = "Get-AcceptedDomain";

    private static readonly Dictionary<string, string> Endpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        [GetMailbox] = "Mailbox",
        [GetMailboxFolderPermission] = "MailboxFolderPermission",
        [GetDistributionGroupMember] = "DistributionGroupMember",
        [GetOrganizationConfig] = "OrganizationConfig",
        [GetAcceptedDomain] = "AcceptedDomain",
    };

    /// <summary>Every cmdlet the backend runs.</summary>
    public static IReadOnlyList<string> Allowed { get; } =
        [GetMailbox, GetMailboxFolderPermission, GetDistributionGroupMember, GetOrganizationConfig, GetAcceptedDomain];

    /// <summary>The endpoint a cmdlet is sent to.</summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.CmdletNotAllowed"/> failure for a cmdlet not on the list.</exception>
    public static string Endpoint(string? cmdlet)
    {
        if (cmdlet is not null && Endpoints.TryGetValue(cmdlet, out string? endpoint))
        {
            return endpoint;
        }

        throw new AegisException(
            FailureKind.CmdletNotAllowed,
            $"'{cmdlet}' is not a read of the Exchange Online Admin API backend; nothing was sent",
            $"the backend runs {string.Join(", ", Allowed)} only (D-004)");
    }
}
