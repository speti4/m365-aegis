using System.Globalization;
using System.Text.RegularExpressions;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>
/// The mailbox lookups over the Exchange Online sidecar (tasks 015 and 016), each a few bulk calls of the
/// <c>Get-EXO*</c> cmdlets, never a call per mailbox from the core: the module batches piped input and retries a
/// throttled request on its own. Every lookup reads only the properties it shows; the permission lists keep the
/// direct, allowed grants and leave out the mailbox's own account (<c>NT AUTHORITY\SELF</c>) and the system's.
/// A grant through a group is listed as the group; its members are not expanded.
/// </summary>
public static partial class MailboxLookups
{
    private const string Unlimited = "Unlimited";
    private const string SharedMailbox = "SharedMailbox";

    private static readonly string[] SendAsRight = ["SendAs"];
    private static readonly string[] SharedOnly = [SharedMailbox];

    /// <summary>The properties whose values name an object in a permission entry or a Send on Behalf list.</summary>
    private static readonly string[] KeyProperties =
        ["Name", "Alias", "DisplayName", "Identity", "PrimarySmtpAddress", "UserPrincipalName", "ExternalDirectoryObjectId", "DistinguishedName"];

    private static readonly string[] MailboxProperties =
        ["DisplayName", "PrimarySmtpAddress", "RecipientTypeDetails", "UserPrincipalName", "ExternalDirectoryObjectId", "Name", "Identity", "Alias"];

    private static readonly string[] RecipientProperties =
        ["Name", "Alias", "DisplayName", "Identity", "PrimarySmtpAddress", "ExternalDirectoryObjectId", "DistinguishedName", "RecipientTypeDetails"];

    /// <summary><c>mailbox list [--shared]</c>: every mailbox (or every shared one), by display name.</summary>
    public static async Task<CommandResult<MailboxList>> ListAsync(IExoCmdletRunner exo, bool sharedOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exo);
        IReadOnlyList<ExoObject> mailboxes = await AllMailboxesAsync(exo, sharedOnly, [], cancellationToken).ConfigureAwait(false);
        return CommandResult.Success(new MailboxList(mailboxes.Select(ToMailbox).OrderBy(mailbox => mailbox.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(), sharedOnly));
    }

    /// <summary>
    /// <c>mailbox access &lt;mailbox&gt;</c>: who holds FullAccess, Send As and Send on Behalf on one mailbox. A
    /// Send on Behalf holder is stored as a name; it is resolved to its address in one more call.
    /// </summary>
    public static async Task<CommandResult<MailboxAccess>> AccessAsync(IExoCmdletRunner exo, string mailbox, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exo);
        string identity = RequireName(mailbox, "the mailbox");
        ExoObject found = await OneMailboxAsync(exo, identity, ["GrantSendOnBehalfTo"], cancellationToken).ConfigureAwait(false);
        MailboxInfo info = ToMailbox(found);
        string key = info.ObjectId ?? info.PrimarySmtpAddress;

        var holders = new List<MailboxHolder>();
        ExoResult fullAccess = await exo.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetMailboxPermission)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = key },
                Properties = ["User", "AccessRights", "IsInherited", "Deny"],
            },
            cancellationToken).ConfigureAwait(false);
        holders.AddRange(fullAccess.Items.Where(IsDirectFullAccess).Select(item => Holder(MailboxRight.FullAccess, item.Text("User")!)));

        ExoResult sendAs = await exo.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetRecipientPermission)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = key, ["AccessRights"] = SendAsRight },
                Properties = ["Trustee", "AccessRights", "AccessControlType", "IsInherited"],
            },
            cancellationToken).ConfigureAwait(false);
        holders.AddRange(sendAs.Items.Where(IsDirectSendAs).Select(item => Holder(MailboxRight.SendAs, item.Text("Trustee")!)));

        IReadOnlyList<string> onBehalf = found.Texts("GrantSendOnBehalfTo");
        if (onBehalf.Count > 0)
        {
            IReadOnlyList<ExoObject> recipients = await RecipientsAsync(exo, onBehalf, cancellationToken).ConfigureAwait(false);
            foreach (string name in onBehalf)
            {
                ExoObject? recipient = recipients.FirstOrDefault(candidate => Keys(candidate).Contains(name));
                holders.Add(new MailboxHolder(MailboxRight.SendOnBehalf, name, recipient?.Text("PrimarySmtpAddress")));
            }
        }

        return CommandResult.Success(new MailboxAccess(
            info,
            holders.OrderBy(holder => holder.Right).ThenBy(holder => holder.Holder, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    /// <summary>
    /// <c>mailbox access --user &lt;upn&gt;</c>, the reverse lookup: every mailbox the user holds FullAccess, Send As
    /// or Send on Behalf on. Four bulk calls whatever the tenant's size: the user as a recipient (its names), every
    /// mailbox with its Send on Behalf list, the FullAccess entries of every mailbox (the mailboxes piped into
    /// <c>Get-EXOMailboxPermission</c>), and the Send As entries of the organization. A mailbox whose permissions
    /// could not be read makes the answer partial (exit code 6) with the others still listed.
    /// </summary>
    public static async Task<CommandResult<UserAccess>> AccessForUserAsync(IExoCmdletRunner exo, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exo);
        string name = RequireName(user, "--user");

        // The names Exchange may use for the user in a permission entry: the user principal name, the name, the
        // display name, the address, the object id. A user Exchange does not know as a recipient keeps the name given.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        string? displayName = null;
        IReadOnlyList<ExoObject> recipient = await RecipientsAsync(exo, [name], cancellationToken).ConfigureAwait(false);
        foreach (ExoObject item in recipient)
        {
            names.UnionWith(Keys(item));
            displayName ??= item.Text("DisplayName");
        }

        IReadOnlyList<ExoObject> mailboxes = await AllMailboxesAsync(exo, sharedOnly: false, ["GrantSendOnBehalfTo"], cancellationToken).ConfigureAwait(false);
        var byKey = new Dictionary<string, MailboxInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (ExoObject mailbox in mailboxes)
        {
            MailboxInfo info = ToMailbox(mailbox);
            foreach (string key in Keys(mailbox))
            {
                byKey.TryAdd(key, info);
            }

            if (string.Equals(info.UserPrincipalName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(info.PrimarySmtpAddress, name, StringComparison.OrdinalIgnoreCase))
            {
                names.UnionWith(Keys(mailbox));
                displayName ??= info.DisplayName;
            }
        }

        var grants = new List<UserGrant>();
        var failed = new List<string>();
        if (mailboxes.Count > 0)
        {
            ExoResult fullAccess = await exo.InvokeAsync(
                new ExoCommand(ExoCmdlets.GetMailboxPermission)
                {
                    Input = mailboxes.Select(mailbox => mailbox.Text("ExternalDirectoryObjectId") ?? mailbox.Text("PrimarySmtpAddress") ?? "").Where(key => key.Length > 0).ToList(),
                    Properties = ["Identity", "User", "AccessRights", "IsInherited", "Deny"],
                },
                cancellationToken).ConfigureAwait(false);
            failed.AddRange(fullAccess.Errors);
            grants.AddRange(fullAccess.Items
                .Where(item => IsDirectFullAccess(item) && names.Contains(item.Text("User")!))
                .Select(item => new UserGrant(MailboxRight.FullAccess, Resolve(byKey, item.Text("Identity")))));
        }

        ExoResult sendAs = await exo.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetRecipientPermission)
            {
                Parameters = new Dictionary<string, object?> { ["AccessRights"] = SendAsRight, ["ResultSize"] = Unlimited },
                Properties = ["Identity", "Trustee", "AccessRights", "AccessControlType", "IsInherited"],
            },
            cancellationToken).ConfigureAwait(false);
        failed.AddRange(sendAs.Errors);
        grants.AddRange(sendAs.Items
            .Where(item => IsDirectSendAs(item) && names.Contains(item.Text("Trustee")!))
            .Select(item => new UserGrant(MailboxRight.SendAs, Resolve(byKey, item.Text("Identity")))));

        grants.AddRange(mailboxes
            .Where(mailbox => mailbox.Texts("GrantSendOnBehalfTo").Any(names.Contains))
            .Select(mailbox => new UserGrant(MailboxRight.SendOnBehalf, ToMailbox(mailbox))));

        var result = new UserAccess(
            name,
            displayName,
            grants
                .DistinctBy(grant => (grant.Right, grant.Mailbox.PrimarySmtpAddress))
                .OrderBy(grant => grant.Mailbox.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Right)
                .ToList(),
            mailboxes.Count,
            failed);
        return failed.Count == 0
            ? CommandResult.Success(result)
            : CommandResult.Failure(Partial(failed.Count, "mailbox permission list(s)"), result);
    }

    /// <summary>
    /// <c>mailbox forwarders</c>: every mailbox with <c>ForwardingSmtpAddress</c> or <c>ForwardingAddress</c> set,
    /// whether it keeps a copy (<c>DeliverToMailboxAndForward</c>), and whether the target is outside the accepted
    /// domains. A recipient target (a mail contact, another mailbox) is resolved to its address in one more call.
    /// </summary>
    public static async Task<CommandResult<MailboxForwarders>> ForwardersAsync(IExoCmdletRunner exo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exo);
        ExoResult domainsResult = await exo.InvokeAsync(new ExoCommand(ExoCmdlets.GetAcceptedDomain) { Properties = ["DomainName"] }, cancellationToken).ConfigureAwait(false);
        List<string> domains = domainsResult.Items.Select(item => item.Text("DomainName")).OfType<string>().ToList();
        IReadOnlyList<ExoObject> mailboxes = await AllMailboxesAsync(exo, sharedOnly: false, ["ForwardingSmtpAddress", "ForwardingAddress", "DeliverToMailboxAndForward"], cancellationToken).ConfigureAwait(false);
        List<ExoObject> forwarding = mailboxes.Where(mailbox => mailbox.Text("ForwardingSmtpAddress") is not null || mailbox.Text("ForwardingAddress") is not null).ToList();

        List<string> recipientTargets = forwarding.Select(mailbox => mailbox.Text("ForwardingAddress")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<ExoObject> recipients = recipientTargets.Count == 0 ? [] : await RecipientsAsync(exo, recipientTargets, cancellationToken).ConfigureAwait(false);

        var forwarders = new List<Forwarder>();
        foreach (ExoObject mailbox in forwarding)
        {
            MailboxInfo info = ToMailbox(mailbox);
            bool keepsCopy = mailbox.Flag("DeliverToMailboxAndForward") ?? false;
            if (mailbox.Text("ForwardingSmtpAddress") is { } smtp)
            {
                string address = WithoutPrefix(smtp);
                forwarders.Add(new Forwarder(info, ForwardingKind.SmtpAddress, smtp, address, IsExternal(address, domains), keepsCopy));
            }

            if (mailbox.Text("ForwardingAddress") is { } target)
            {
                string? address = recipients.FirstOrDefault(candidate => Keys(candidate).Contains(target))?.Text("PrimarySmtpAddress");
                forwarders.Add(new Forwarder(info, ForwardingKind.Recipient, target, address, address is null ? null : IsExternal(address, domains), keepsCopy));
            }
        }

        return CommandResult.Success(new MailboxForwarders(
            forwarders
                .OrderByDescending(forwarder => forwarder.External ?? true)
                .ThenBy(forwarder => forwarder.Mailbox.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            domains,
            mailboxes.Count));
    }

    /// <summary>
    /// <c>mailbox size [&lt;mailbox&gt;] [--top N]</c>: the live size and item count of one mailbox, or of every
    /// mailbox (piped into <c>Get-EXOMailboxStatistics</c>), the largest first.
    /// </summary>
    public static async Task<CommandResult<MailboxSizes>> SizeAsync(IExoCmdletRunner exo, string? mailbox, int? top, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exo);
        if (top is <= 0)
        {
            throw new AegisException(FailureKind.Usage, $"--top must be 1 or more, not {top}", "leave --top out to list every mailbox");
        }

        string[] extra = ["ExchangeGuid"];
        IReadOnlyList<ExoObject> mailboxes = string.IsNullOrWhiteSpace(mailbox)
            ? await AllMailboxesAsync(exo, sharedOnly: false, extra, cancellationToken).ConfigureAwait(false)
            : [await OneMailboxAsync(exo, mailbox.Trim(), extra, cancellationToken).ConfigureAwait(false)];

        var failed = new List<string>();
        var sizes = new List<MailboxSize>();
        if (mailboxes.Count > 0)
        {
            ExoResult statistics = await exo.InvokeAsync(
                new ExoCommand(ExoCmdlets.GetMailboxStatistics)
                {
                    Input = mailboxes.Select(item => item.Text("ExternalDirectoryObjectId") ?? item.Text("PrimarySmtpAddress") ?? "").Where(key => key.Length > 0).ToList(),
                    Properties = ["DisplayName", "MailboxGuid", "ItemCount", "TotalItemSize", "DeletedItemCount", "TotalDeletedItemSize"],
                },
                cancellationToken).ConfigureAwait(false);
            failed.AddRange(statistics.Errors);
            var byGuid = mailboxes.Where(item => item.Text("ExchangeGuid") is not null)
                .GroupBy(item => item.Text("ExchangeGuid")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (ExoObject item in statistics.Items)
            {
                ExoObject? owner = item.Text("MailboxGuid") is { } guid && byGuid.TryGetValue(guid, out ExoObject? byId)
                    ? byId
                    : mailboxes.FirstOrDefault(candidate => string.Equals(candidate.Text("DisplayName"), item.Text("DisplayName"), StringComparison.OrdinalIgnoreCase));
                MailboxInfo info = owner is not null
                    ? ToMailbox(owner)
                    : new MailboxInfo(item.Text("DisplayName") ?? "?", "-", "?", null, null);
                sizes.Add(new MailboxSize(
                    info,
                    item.Number("ItemCount") ?? 0,
                    Bytes(item.Text("TotalItemSize")),
                    item.Text("TotalItemSize"),
                    item.Number("DeletedItemCount") ?? 0,
                    Bytes(item.Text("TotalDeletedItemSize"))));
            }
        }

        IEnumerable<MailboxSize> ordered = sizes
            .OrderByDescending(size => size.TotalItemBytes ?? -1)
            .ThenBy(size => size.Mailbox.DisplayName, StringComparer.OrdinalIgnoreCase);
        var result = new MailboxSizes((top is { } limit ? ordered.Take(limit) : ordered).ToList(), mailboxes.Count, failed);
        return failed.Count == 0
            ? CommandResult.Success(result)
            : CommandResult.Failure(Partial(failed.Count, "mailbox size(s)"), result);
    }

    /// <summary>The bytes of a size as the service writes it (<c>1.2 GB (1,288,490,188 bytes)</c>), or null.</summary>
    public static long? Bytes(string? size)
    {
        if (size is null)
        {
            return null;
        }

        Match match = BytesPattern().Match(size);
        if (!match.Success)
        {
            return null;
        }

        string digits = new(match.Groups[1].Value.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) ? bytes : null;
    }

    [GeneratedRegex(@"\(([\d,.\s  ']+) bytes\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BytesPattern();

    private static async Task<IReadOnlyList<ExoObject>> AllMailboxesAsync(IExoCmdletRunner exo, bool sharedOnly, string[] extra, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> { ["ResultSize"] = Unlimited };
        if (sharedOnly)
        {
            parameters["RecipientTypeDetails"] = SharedOnly;
        }

        if (extra.Length > 0)
        {
            parameters["Properties"] = extra;
        }

        ExoResult result = await exo.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetMailbox) { Parameters = parameters, Properties = [.. MailboxProperties, .. extra] },
            cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    private static async Task<ExoObject> OneMailboxAsync(IExoCmdletRunner exo, string identity, string[] extra, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> { ["Identity"] = identity };
        if (extra.Length > 0)
        {
            parameters["Properties"] = extra;
        }

        ExoResult result;
        try
        {
            result = await exo.InvokeAsync(
                new ExoCommand(ExoCmdlets.GetMailbox) { Parameters = parameters, Properties = [.. MailboxProperties, .. extra] },
                cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            // The service's sentence names its own server; the answer names the mailbox as it was given.
            throw new AegisException(FailureKind.NotFound, $"mailbox '{identity}' not found", SidecarFailures.NotFoundHint, exception);
        }

        return result.Items.Count > 0
            ? result.Items[0]
            : throw new AegisException(FailureKind.NotFound, $"mailbox '{identity}' not found", SidecarFailures.NotFoundHint);
    }

    /// <summary>The recipients the names resolve to, piped in one call; a name that resolves to none is left out.</summary>
    private static async Task<IReadOnlyList<ExoObject>> RecipientsAsync(IExoCmdletRunner exo, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        try
        {
            ExoResult result = await exo.InvokeAsync(new ExoCommand(ExoCmdlets.GetRecipient) { Input = names, Properties = RecipientProperties }, cancellationToken).ConfigureAwait(false);
            return result.Items;
        }
        catch (AegisException exception) when (exception.Failure.Kind is FailureKind.NotFound or FailureKind.CmdletFailed)
        {
            // Resolving a name is a courtesy: the holder or the target is still listed as Exchange stores it.
            return [];
        }
    }

    private static MailboxInfo ToMailbox(ExoObject item) => new(
        item.Text("DisplayName") ?? item.Text("Name") ?? "?",
        item.Text("PrimarySmtpAddress") ?? "-",
        item.Text("RecipientTypeDetails") ?? "?",
        item.Text("UserPrincipalName"),
        item.Text("ExternalDirectoryObjectId"));

    /// <summary>The names an object answers to in a permission entry or a Send on Behalf list.</summary>
    private static HashSet<string> Keys(ExoObject item)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in KeyProperties)
        {
            if (item.Text(name) is { } value)
            {
                keys.Add(value);
            }
        }

        return keys;
    }

    private static MailboxInfo Resolve(Dictionary<string, MailboxInfo> byKey, string? identity) =>
        identity is not null && byKey.TryGetValue(identity, out MailboxInfo? info)
            ? info
            : new MailboxInfo(identity ?? "?", "-", "?", null, null);

    private static MailboxHolder Holder(MailboxRight right, string holder) =>
        new(right, holder, holder.Contains('@', StringComparison.Ordinal) ? holder : null);

    private static bool IsDirectFullAccess(ExoObject item) =>
        item.Flag("IsInherited") != true
        && item.Flag("Deny") != true
        && item.Texts("AccessRights").Any(right => right.Contains("FullAccess", StringComparison.OrdinalIgnoreCase))
        && IsPerson(item.Text("User"));

    private static bool IsDirectSendAs(ExoObject item) =>
        item.Flag("IsInherited") != true
        && !string.Equals(item.Text("AccessControlType"), "Deny", StringComparison.OrdinalIgnoreCase)
        && item.Texts("AccessRights").Any(right => right.Contains("SendAs", StringComparison.OrdinalIgnoreCase))
        && IsPerson(item.Text("Trustee"));

    /// <summary>A holder that is an account or a group, not the mailbox's own account or the system's.</summary>
    private static bool IsPerson(string? holder) =>
        holder is not null && !holder.StartsWith(@"NT AUTHORITY\", StringComparison.OrdinalIgnoreCase);

    private static string WithoutPrefix(string address) =>
        address.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase) ? address[5..] : address;

    /// <summary>Whether an address is outside the accepted domains (a <c>*.contoso.com</c> entry accepts the subdomains).</summary>
    internal static bool IsExternal(string address, IReadOnlyList<string> domains)
    {
        int at = address.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        string domain = address[(at + 1)..];
        return !domains.Any(accepted => accepted.StartsWith("*.", StringComparison.Ordinal)
            ? domain.EndsWith(accepted[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(domain, accepted, StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireName(string? value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AegisException(FailureKind.Usage, $"{what} is missing", "name a mailbox by its address, its name or its object id")
            : value.Trim();

    private static AegisFailure Partial(int count, string what) =>
        new(FailureKind.Partial, $"{count} {what} could not be read; the answer lists the rest", "the data's `failed` list has the service's messages; run the command again for the rest");
}
