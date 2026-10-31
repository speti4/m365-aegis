using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>
/// The mailbox lookups over the three layers of D-004 (tasks 042 to 044): each read goes to the layer that answers
/// it best. The Admin API reads one mailbox with its Send on Behalf delegates by name, a folder's permissions, a
/// distribution group's members and the organization's configuration; the sidecar reads FullAccess, Send As and the
/// bulk lists; Graph expands a group into its members and reads the mailbox usage report. A read the Admin API
/// cannot answer (not enabled, a refused permission) falls back to the sidecar where the sidecar has it.
/// </summary>
public static partial class MailboxLookups
{
    private const string Calendar = "Calendar";

    private static readonly string[] PermissionProperties = ["Identity", "User", "AccessRights", "IsInherited", "Deny"];

    /// <summary>
    /// <c>mailbox list [--shared]</c> over the layers (task 048): the Admin API's paged <c>Get-Mailbox</c> with
    /// <c>$select</c> on the list's fields, no sidecar; the sidecar's <c>Get-EXOMailbox</c> when the run has no Admin
    /// API or the Admin API cannot answer (not enabled, a refused permission, a service error). The endpoint takes no
    /// <c>-RecipientTypeDetails</c> (https://learn.microsoft.com/exchange/reference/admin-api-endpoints-mailbox), so
    /// <paramref name="sharedOnly"/> keeps the shared mailboxes of the whole list.
    /// </summary>
    public static async Task<CommandResult<MailboxList>> ListAsync(ExchangeBackends backends, bool sharedOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        if (backends.HasAdminApi)
        {
            try
            {
                IReadOnlyList<ExoObject> mailboxes = await AdminMailboxesAsync(backends.AdminApi!, cancellationToken).ConfigureAwait(false);
                IEnumerable<ExoObject> kept = sharedOnly
                    ? mailboxes.Where(mailbox => string.Equals(mailbox.Text("RecipientTypeDetails"), SharedMailbox, StringComparison.OrdinalIgnoreCase))
                    : mailboxes;
                return CommandResult.Success(new MailboxList(kept.Select(ToMailbox).OrderBy(mailbox => mailbox.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(), sharedOnly));
            }
            catch (AegisException exception) when (exception.Failure.Kind is not (FailureKind.Auth or FailureKind.Usage))
            {
                // A sign-in that failed fails the sidecar too; anything else the sidecar may still answer.
                LogSink.Verbose?.Write("admin-api", $"the Admin API could not list the mailboxes ({exception.Failure.Code}: {exception.Message}); the sidecar answers instead");
            }
        }

        return await ListAsync(backends.Sidecar, sharedOnly, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>mailbox access &lt;mailbox&gt; [--expand-groups]</c> over the layers: the mailbox and its Send on Behalf
    /// delegates with their display names from the Admin API (task 042) while the sidecar reads FullAccess and Send
    /// As, side by side; the sidecar alone when the run has no Admin API or the Admin API cannot answer. With
    /// <see cref="MailboxAccessOptions.ExpandGroups"/> a group holder lists its members (task 044): one piped
    /// <c>Get-EXORecipient</c> tells the groups among the holders, and Graph's transitive members of each are read in
    /// two rounds of <c>$batch</c>.
    /// </summary>
    public static async Task<CommandResult<MailboxAccess>> AccessAsync(ExchangeBackends backends, string mailbox, MailboxAccessOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(options);
        string identity = RequireName(mailbox, "the mailbox");
        MailboxAccess? access = backends.HasAdminApi ? await AccessThroughAdminApiAsync(backends, identity, cancellationToken).ConfigureAwait(false) : null;
        access ??= ThroughSidecar((await AccessAsync(backends.Sidecar, identity, cancellationToken).ConfigureAwait(false)).Data!);
        if (options.ExpandGroups)
        {
            access = await ExpandHoldersAsync(backends, access, cancellationToken).ConfigureAwait(false);
        }

        return CommandResult.Success(access);
    }

    /// <summary>The sidecar's answer with its layer named on the mailbox and on every grant (task 063).</summary>
    private static MailboxAccess ThroughSidecar(MailboxAccess access) => access with
    {
        MailboxLayer = Backend.Pwsh,
        Holders = access.Holders.Select(holder => holder with { Layer = Backend.Pwsh }).ToList(),
    };

    /// <summary>
    /// <c>mailbox access --user &lt;upn&gt; [--expand-groups] [--method …]</c> over the layers: the reverse lookup of
    /// task 016 with the FullAccess entries read the way <see cref="UserAccessOptions.Method"/> names (task 043), and
    /// with <see cref="UserAccessOptions.ExpandGroups"/> the grants the user holds through a group too (task 044): the
    /// user's groups at every nesting level from Graph, matched against the holders by name, and a holder no name
    /// matched resolved with one piped <c>Get-EXORecipient</c> to its object id.
    /// </summary>
    public static Task<CommandResult<UserAccess>> AccessForUserAsync(ExchangeBackends backends, string user, UserAccessOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(options);
        return ReverseAsync(backends.Sidecar, backends, user, options, cancellationToken);
    }

    /// <summary>
    /// <c>mailbox size [&lt;mailbox&gt;] [--top N] [--method …]</c> over the layers (task 043): from Graph's mailbox
    /// usage report (one call, as of the report's day, a few days old) joined to the mailbox list of the Admin API
    /// (the sidecar's when the run has none), or live through the sidecar, piped or split across several sidecars.
    /// Without a method, every mailbox's size comes from the report and one mailbox's live.
    /// </summary>
    public static Task<CommandResult<MailboxSizes>> SizeAsync(ExchangeBackends backends, string? mailbox, int? top, MailboxSizeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(options);
        MailboxSizeMethod method = options.Method ?? (string.IsNullOrWhiteSpace(mailbox) ? MailboxSizeMethod.Report : MailboxSizeMethod.Piped);
        return method == MailboxSizeMethod.Report
            ? ReportSizesAsync(backends, mailbox, top, cancellationToken)
            : SizesAsync(backends.Sidecar, backends, mailbox, top, options with { Method = method }, cancellationToken);
    }

    /// <summary>
    /// <c>mailbox folder-permissions &lt;mailbox&gt; [--folder &lt;path&gt;]</c> (task 042): who holds which right on one
    /// folder of a mailbox, the calendar by default, through the Admin API's <c>MailboxFolderPermission</c> endpoint
    /// (<c>Get-MailboxFolderPermission -Identity &lt;mailbox&gt;:\&lt;folder&gt;</c>). The folder is named as the mailbox's
    /// own language names it.
    /// </summary>
    public static async Task<CommandResult<MailboxFolderPermissions>> FolderPermissionsAsync(ExchangeBackends backends, string mailbox, string? folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        string box = RequireName(mailbox, "the mailbox");
        IExoCmdletRunner admin = RequireAdminApi(backends);
        string path = string.IsNullOrWhiteSpace(folder)
            ? await CalendarPathAsync(backends, box, cancellationToken).ConfigureAwait(false)
            : folder.Trim().Replace('/', '\\').Trim('\\');
        ExoResult result;
        try
        {
            result = await admin.InvokeAsync(
                new ExoCommand(AdminApiCmdlets.GetMailboxFolderPermission)
                {
                    Parameters = new Dictionary<string, object?> { ["Identity"] = $"{box}:\\{path}", ["ResultSize"] = Unlimited },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw new AegisException(
                FailureKind.NotFound,
                $"folder '{path}' of mailbox '{box}' not found",
                "check the mailbox's address, and name the folder as the mailbox's own language does (--folder): the default calendar is Calendar in an English mailbox only",
                exception);
        }

        List<FolderPermissionEntry> entries = result.Items
            .Select(item => new FolderPermissionEntry(
                Name(item, "User") ?? "?",
                Words(item, "AccessRights", keepNone: true),
                Words(item, "SharingPermissionFlags", keepNone: false)))
            .ToList();
        string? folderName = result.Items.Select(item => item.Text("FolderName")).FirstOrDefault(name => name is not null);
        return CommandResult.Success(new MailboxFolderPermissions(box, path, folderName, entries));
    }

    /// <summary>
    /// The default calendar's path in the mailbox's own language (<c>Naptár</c>, <c>Kalender</c>): the folder path
    /// <c>Get-MailboxFolderPermission</c> takes is the localized name, and the cmdlet documents no language-free form
    /// (live, 2026-10-30: <c>:\Calendar</c> is not found in a Hungarian mailbox). One sidecar call,
    /// <c>Get-EXOMailboxFolderStatistics -FolderScope Calendar</c>, names the folder whose type is <c>Calendar</c>;
    /// the English <c>Calendar</c> answers only when that call cannot (a missing mailbox is still not found).
    /// </summary>
    private static async Task<string> CalendarPathAsync(ExchangeBackends backends, string mailbox, CancellationToken cancellationToken)
    {
        try
        {
            ExoResult folders = await backends.Sidecar.InvokeAsync(
                new ExoCommand(ExoCmdlets.GetMailboxFolderStatistics)
                {
                    Parameters = new Dictionary<string, object?> { ["Identity"] = mailbox, ["FolderScope"] = Calendar },
                    Properties = ["Name", "FolderPath", "FolderType"],
                },
                cancellationToken).ConfigureAwait(false);
            string? path = folders.Items
                .Where(item => string.Equals(item.Text("FolderType"), Calendar, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Text("FolderPath"))
                .OfType<string>()
                .OrderBy(found => found.Length)
                .FirstOrDefault();
            if (path is not null)
            {
                return path.Replace('/', '\\').Trim('\\');
            }
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw MailboxNotFound(mailbox, exception);
        }
        catch (AegisException exception) when (exception.Failure.Kind is FailureKind.CmdletFailed or FailureKind.Forbidden)
        {
            LogSink.Verbose?.Write("exchange-online", $"the calendar's name could not be read ({exception.Failure.Code}); the English name is tried");
        }

        return Calendar;
    }

    /// <summary>
    /// <c>mailbox group-members &lt;group&gt;</c> (task 042): a distribution group's direct members as Exchange Online
    /// keeps them, the hidden ones included, through the Admin API's <c>DistributionGroupMember</c> endpoint over
    /// every page.
    /// </summary>
    public static async Task<CommandResult<DistributionGroupMembers>> DistributionGroupMembersAsync(ExchangeBackends backends, string group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        string name = RequireName(group, "the group");
        IExoCmdletRunner admin = RequireAdminApi(backends);
        ExoResult result;
        try
        {
            result = await admin.InvokeAsync(
                new ExoCommand(AdminApiCmdlets.GetDistributionGroupMember)
                {
                    Parameters = new Dictionary<string, object?> { ["Identity"] = name, ["ResultSize"] = Unlimited },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw new AegisException(
                FailureKind.NotFound,
                $"distribution group '{name}' not found",
                "give the group's address, name or GUID; `m365-aegis group members` reads any group through Graph, Microsoft 365 and security groups too",
                exception);
        }

        List<DistributionMember> members = result.Items
            .Select(item => new DistributionMember(
                item.Text("DisplayName") ?? item.Text("Name") ?? "?",
                item.Text("PrimarySmtpAddress"),
                item.Text("RecipientTypeDetails") ?? "?",
                item.Flag("HiddenFromAddressListsEnabled") ?? false))
            .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.PrimarySmtpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(new DistributionGroupMembers(name, members));
    }

    /// <summary>
    /// <c>mailbox organization</c> (task 042): the organization's configuration the Admin API documents (its name and the
    /// MailTips settings) and its accepted domains, two calls side by side.
    /// </summary>
    public static async Task<CommandResult<ExchangeOrganization>> OrganizationAsync(ExchangeBackends backends, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        IExoCmdletRunner admin = RequireAdminApi(backends);
        Task<ExoResult> config = admin.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetOrganizationConfig), cancellationToken);
        Task<ExoResult> domains = admin.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetAcceptedDomain), cancellationToken);
        await Task.WhenAll(config, domains).ConfigureAwait(false);
        IReadOnlyList<ExoObject> configs = (await config.ConfigureAwait(false)).Items;
        ExoObject? item = configs.Count > 0 ? configs[0] : null;
        List<AcceptedDomainInfo> accepted = (await domains.ConfigureAwait(false)).Items
            .Where(domain => domain.Text("DomainName") is not null)
            .Select(domain => new AcceptedDomainInfo(domain.Text("DomainName")!, domain.Text("DomainType"), domain.Flag("Default")))
            .OrderByDescending(domain => domain.Default == true)
            .ThenBy(domain => domain.DomainName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(new ExchangeOrganization(
            item?.Text("Name"),
            item?.Flag("MailTipsAllTipsEnabled"),
            item?.Flag("MailTipsExternalRecipientsTipsEnabled"),
            item?.Number("MailTipsLargeAudienceThreshold"),
            accepted));
    }

    /// <summary>
    /// A Send on Behalf delegate as <c>GrantSendOnBehalfToWithDisplayNames</c> carries it, as the holder (its display
    /// name, else its address, else the entry as it came) and its address when the entry has one. The Admin API
    /// documents the list but not its entries' shape, so an object (<c>DisplayName</c>, <c>PrimarySmtpAddress</c>, …)
    /// and a text with an address in it (<c>Bob Example &lt;bob@contoso.com&gt;</c>, <c>Bob Example (bob@contoso.com)</c>)
    /// are both read.
    /// </summary>
    internal static (string Holder, string? Address) Delegate(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string text = entry.Trim();
        string? name = null;
        string? address = null;
        if (text.StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                JsonElement root = document.RootElement;
                name = First(root, "DisplayName", "Name");
                address = First(root, "PrimarySmtpAddress", "SmtpAddress", "EmailAddress", "Address", "Email", "UserPrincipalName")
                    ?? root.EnumerateObject().Select(property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null).FirstOrDefault(value => value?.Contains('@', StringComparison.Ordinal) == true);
            }
            catch (JsonException)
            {
                // Not an object after all: read as text below.
            }
        }

        if (name is null && address is null)
        {
            Match match = AddressPattern().Match(text);
            address = match.Success ? match.Value : null;
            string rest = match.Success ? text.Remove(match.Index, match.Length) : text;
            rest = rest.Trim(' ', '<', '>', '(', ')', '[', ']', ';', ',', ':', '|', '-', '"', '\'');
            name = rest.Length > 0 ? rest : null;
        }

        if (address is not null && address.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase))
        {
            address = address[5..];
        }

        return (name ?? address ?? text, address);

        static string? First(JsonElement root, params string[] names) =>
            names.Select(candidate => root.TryGetProperty(candidate, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    [GeneratedRegex(@"(smtp:)?[^\s<>()\[\];,""']+@[^\s<>()\[\];,""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    /// <summary>FullAccess and Send As on one mailbox from the sidecar; a mailbox it does not find is named as given.</summary>
    private static async Task<List<MailboxHolder>> RightsAsync(IExoCmdletRunner exo, string key, string identity, CancellationToken cancellationToken)
    {
        var holders = new List<MailboxHolder>();
        try
        {
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
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw MailboxNotFound(identity, exception);
        }

        return holders;
    }

    /// <summary>
    /// The mailbox and its delegates from the Admin API and its rights from the sidecar, side by side; null when the
    /// Admin API could not answer for a reason other than a missing mailbox, so the sidecar answers the whole read.
    /// </summary>
    private static async Task<MailboxAccess?> AccessThroughAdminApiAsync(ExchangeBackends backends, string identity, CancellationToken cancellationToken)
    {
        IExoCmdletRunner admin = backends.AdminApi!;
        IExoCmdletRunner sidecar = backends.Sidecar;
        Task<ExoObject> mailboxRead = AdminMailboxAsync(admin, identity, displayNames: true, cancellationToken);
        Task<List<MailboxHolder>> rightsRead = RightsAsync(sidecar, identity, identity, cancellationToken);
        try
        {
            await Task.WhenAll(mailboxRead, rightsRead).ConfigureAwait(false);
        }
        catch (AegisException) when (mailboxRead.IsFaulted || rightsRead.IsFaulted)
        {
            // Each read's failure is looked at below.
        }

        if (mailboxRead.IsFaulted)
        {
            AegisException failure = Unwrap(mailboxRead);
            _ = rightsRead.Exception;
            if (failure.Failure.Kind == FailureKind.NotFound)
            {
                throw failure;
            }

            LogSink.Verbose?.Write("admin-api", $"the Admin API could not read the mailbox ({failure.Failure.Code}: {failure.Message}); the sidecar answers instead");
            return null;
        }

        if (rightsRead.IsFaulted)
        {
            ExceptionDispatchInfo.Capture(Unwrap(rightsRead)).Throw();
        }

        ExoObject found = await mailboxRead.ConfigureAwait(false);
        List<MailboxHolder> holders = (await rightsRead.ConfigureAwait(false)).Select(holder => holder with { Layer = Backend.Pwsh }).ToList();
        holders.AddRange((await SendOnBehalfAsync(sidecar, found, cancellationToken).ConfigureAwait(false)).Select(holder => holder with { Layer = Backend.AdminApi }));
        return new MailboxAccess(
            ToMailbox(found),
            holders.OrderBy(holder => holder.Right).ThenBy(holder => holder.Holder, StringComparer.OrdinalIgnoreCase).ToList())
        {
            MailboxLayer = Backend.AdminApi,
        };
    }

    /// <summary>
    /// One mailbox from the Admin API's <c>Mailbox</c> endpoint; with <paramref name="displayNames"/> its Send on
    /// Behalf delegates with their display names, a parameter in staged rollout, asked for again without it when the
    /// service refuses it.
    /// </summary>
    private static async Task<ExoObject> AdminMailboxAsync(IExoCmdletRunner admin, string identity, bool displayNames, CancellationToken cancellationToken)
    {
        ExoResult result;
        try
        {
            var parameters = new Dictionary<string, object?> { ["Identity"] = identity };
            if (displayNames)
            {
                parameters["IncludeGrantSendOnBehalfToWithDisplayNames"] = true;
            }

            result = await admin.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox) { Parameters = parameters }, cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception) when (displayNames && exception.Failure.Kind == FailureKind.CmdletFailed)
        {
            return await AdminMailboxAsync(admin, identity, displayNames: false, cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw MailboxNotFound(identity, exception);
        }

        return result.Items.Count > 0 ? result.Items[0] : throw MailboxNotFound(identity, null);
    }

    /// <summary>
    /// The Send on Behalf holders of an Admin API mailbox: the delegates with their display names when the service
    /// sent them, else <c>GrantSendOnBehalfTo</c>, whose addresses stand as they are and whose names are resolved to
    /// addresses with one piped <c>Get-EXORecipient</c>.
    /// </summary>
    private static async Task<IReadOnlyList<MailboxHolder>> SendOnBehalfAsync(IExoCmdletRunner sidecar, ExoObject mailbox, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> named = mailbox.Texts("GrantSendOnBehalfToWithDisplayNames");
        if (named.Count > 0)
        {
            return named.Select(Delegate).Select(entry => new MailboxHolder(MailboxRight.SendOnBehalf, entry.Holder, entry.Address)).ToList();
        }

        IReadOnlyList<string> plain = mailbox.Texts("GrantSendOnBehalfTo");
        List<string> names = plain.Where(entry => !entry.Contains('@', StringComparison.Ordinal)).ToList();
        IReadOnlyList<ExoObject> recipients = names.Count == 0 ? [] : await RecipientsAsync(sidecar, names, cancellationToken).ConfigureAwait(false);
        return plain.Select(entry => entry.Contains('@', StringComparison.Ordinal)
                ? new MailboxHolder(MailboxRight.SendOnBehalf, entry, entry)
                : new MailboxHolder(MailboxRight.SendOnBehalf, entry, recipients.FirstOrDefault(candidate => Keys(candidate).Contains(entry))?.Text("PrimarySmtpAddress")))
            .ToList();
    }

    /// <summary>
    /// The holders with the groups among them marked, and each group's members over every nesting level from Graph
    /// (task 044); a group Graph does not hold (no object id: a dynamic distribution group) is marked but not expanded.
    /// </summary>
    private static async Task<MailboxAccess> ExpandHoldersAsync(ExchangeBackends backends, MailboxAccess access, CancellationToken cancellationToken)
    {
        if (access.Holders.Count == 0)
        {
            return access;
        }

        List<string> names = access.Holders.SelectMany(holder => new[] { holder.Holder, holder.Address }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<ExoObject> recipients = await RecipientsAsync(backends.Sidecar, names, cancellationToken).ConfigureAwait(false);
        List<ExoObject?> found = access.Holders
            .Select(holder => recipients.FirstOrDefault(recipient => Keys(recipient).Contains(holder.Holder) || (holder.Address is not null && Keys(recipient).Contains(holder.Address))))
            .ToList();
        List<string> groupIds = found
            .Where(IsGroup)
            .Select(recipient => recipient!.Text("ExternalDirectoryObjectId"))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> members = groupIds.Count > 0 && backends.HasGraph
            ? await GroupLookups.ExpandAsync(backends.Graph, groupIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, IReadOnlyList<GroupMember>>();
        List<MailboxHolder> expanded = access.Holders
            .Select((holder, index) =>
            {
                ExoObject? recipient = found[index];
                bool group = IsGroup(recipient);
                IReadOnlyList<GroupMember>? list = group && recipient!.Text("ExternalDirectoryObjectId") is { } id && members.TryGetValue(id, out IReadOnlyList<GroupMember>? groupMembers)
                    ? groupMembers
                    : null;
                return holder with { Group = group, Members = list };
            })
            .ToList();
        return access with { Holders = expanded };
    }

    /// <summary>Whether a recipient is a group (a mail-enabled security or distribution group, a Microsoft 365 group's mailbox, …).</summary>
    private static bool IsGroup(ExoObject? recipient) =>
        recipient?.Text("RecipientTypeDetails") is { } type && (type.Contains("Group", StringComparison.OrdinalIgnoreCase) || type.Equals("RoomList", StringComparison.OrdinalIgnoreCase));

    /// <summary>The reverse lookup, over the sidecar alone (<paramref name="backends"/> null) or over the layers.</summary>
    private static async Task<CommandResult<UserAccess>> ReverseAsync(IExoCmdletRunner exo, ExchangeBackends? backends, string user, UserAccessOptions options, CancellationToken cancellationToken)
    {
        string name = RequireName(user, "--user");
        RequireSidecars(options.Sidecars);
        // Only the overload over the layers expands the groups, so a run that asks for it always has its backends.
        if (options.ExpandGroups && !backends!.HasGraph)
        {
            throw new InvalidOperationException("--expand-groups needs the Graph client");
        }

        Task<IReadOnlyList<UserGroup>>? groupsRead = options.ExpandGroups ? GroupLookups.MemberOfAsync(backends!.Graph, name, cancellationToken) : null;

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

        // Every direct, allowed entry of every mailbox: who holds which right on which mailbox.
        GrantTable table = await GrantTableAsync(exo, backends, options, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ExoObject> mailboxes = table.Mailboxes;
        List<(MailboxRight Right, string Holder, MailboxInfo Mailbox)> entries = table.Entries;
        List<string> failed = table.Failed;
        foreach (ExoObject mailbox in mailboxes)
        {
            MailboxInfo info = ToMailbox(mailbox);
            if (string.Equals(info.UserPrincipalName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(info.PrimarySmtpAddress, name, StringComparison.OrdinalIgnoreCase))
            {
                names.UnionWith(Keys(mailbox));
                displayName ??= info.DisplayName;
            }
        }

        var grants = entries.Where(entry => names.Contains(entry.Holder)).Select(entry => new UserGrant(entry.Right, entry.Mailbox)).ToList();
        int? groupCount = null;
        if (groupsRead is not null)
        {
            IReadOnlyList<UserGroup> groups = await groupsRead.ConfigureAwait(false);
            groupCount = groups.Count;
            grants.AddRange(await GroupGrantsAsync(exo, groups, entries.Where(entry => !names.Contains(entry.Holder)).ToList(), table.ByKey, cancellationToken).ConfigureAwait(false));
        }

        var result = new UserAccess(
            name,
            displayName,
            grants
                .DistinctBy(grant => (grant.Right, grant.Mailbox.PrimarySmtpAddress))
                .OrderBy(grant => grant.Mailbox.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Right)
                .ToList(),
            mailboxes.Count,
            failed)
        {
            Method = backends is null ? null : Method(options.Method),
            Groups = groupCount,
        };
        return failed.Count == 0
            ? CommandResult.Success(result)
            : CommandResult.Failure(Partial(failed.Count, "mailbox permission list(s)"), result);
    }

    /// <summary>
    /// Every direct, allowed entry of every mailbox (the reverse lookup's table, task 016): who holds which right on
    /// which mailbox, the holder as Exchange names it; the mailboxes with their Send on Behalf lists, every name a
    /// mailbox answers to, and the service's messages of the entries it could not read.
    /// </summary>
    private static async Task<GrantTable> GrantTableAsync(IExoCmdletRunner exo, ExchangeBackends? backends, UserAccessOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<ExoObject> mailboxes = await AllMailboxesAsync(exo, sharedOnly: false, ["GrantSendOnBehalfTo"], cancellationToken).ConfigureAwait(false);
        var byKey = new Dictionary<string, MailboxInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (ExoObject mailbox in mailboxes)
        {
            MailboxInfo info = ToMailbox(mailbox);
            foreach (string key in Keys(mailbox))
            {
                byKey.TryAdd(key, info);
            }
        }

        var entries = new List<(MailboxRight Right, string Holder, MailboxInfo Mailbox)>();
        var failed = new List<string>();
        if (mailboxes.Count > 0)
        {
            ExoResult fullAccess = await FullAccessAsync(exo, backends, mailboxes, options, cancellationToken).ConfigureAwait(false);
            failed.AddRange(fullAccess.Errors);
            entries.AddRange(fullAccess.Items
                .Where(IsDirectFullAccess)
                .Select(item => (MailboxRight.FullAccess, item.Text("User")!, Resolve(byKey, item.Text("Identity")))));
        }

        ExoResult sendAs = await exo.InvokeAsync(
            new ExoCommand(ExoCmdlets.GetRecipientPermission)
            {
                Parameters = new Dictionary<string, object?> { ["AccessRights"] = SendAsRight, ["ResultSize"] = Unlimited },
                Properties = ["Identity", "Trustee", "AccessRights", "AccessControlType", "IsInherited"],
            },
            cancellationToken).ConfigureAwait(false);
        failed.AddRange(sendAs.Errors);
        entries.AddRange(sendAs.Items
            .Where(IsDirectSendAs)
            .Select(item => (MailboxRight.SendAs, item.Text("Trustee")!, Resolve(byKey, item.Text("Identity")))));
        foreach (ExoObject mailbox in mailboxes)
        {
            entries.AddRange(mailbox.Texts("GrantSendOnBehalfTo").Select(holder => (MailboxRight.SendOnBehalf, holder, ToMailbox(mailbox))));
        }

        return new GrantTable(mailboxes, entries, failed, byKey);
    }

    /// <summary>The table of <see cref="GrantTableAsync"/>.</summary>
    private sealed record GrantTable(
        IReadOnlyList<ExoObject> Mailboxes,
        List<(MailboxRight Right, string Holder, MailboxInfo Mailbox)> Entries,
        List<string> Failed,
        Dictionary<string, MailboxInfo> ByKey);

    /// <summary>
    /// Every direct grant of every mailbox for the snapshot (task 087): the reverse lookup's table, read the way
    /// <c>mailbox access --user</c> reads it by default (the FullAccess entries split across sidecars), each holder
    /// with the recipient Exchange knows by that name: a mailbox's own names first, then one piped
    /// <c>Get-EXORecipient</c> for the rest (a security identifier is left as it is). A holder no recipient answers
    /// to has none; the service's messages of the entries it could not read come with the grants.
    /// </summary>
    internal static async Task<MailboxGrants> AllGrantsAsync(ExchangeBackends backends, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        GrantTable table = await GrantTableAsync(backends.Sidecar, backends, new UserAccessOptions(), cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, GrantRecipient>(StringComparer.OrdinalIgnoreCase);
        void Add(ExoObject recipient)
        {
            var found = new GrantRecipient(
                recipient.Text("ExternalDirectoryObjectId"),
                recipient.Text("UserPrincipalName"),
                recipient.Text("PrimarySmtpAddress"),
                recipient.Text("DisplayName") ?? recipient.Text("Name"),
                recipient.Text("RecipientTypeDetails"));
            foreach (string key in Keys(recipient))
            {
                byName.TryAdd(key, found);
            }
        }

        foreach (ExoObject mailbox in table.Mailboxes)
        {
            Add(mailbox);
        }

        List<string> unknown = table.Entries
            .Select(entry => entry.Holder)
            .Where(holder => !byName.ContainsKey(holder) && !holder.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
        {
            foreach (ExoObject recipient in await RecipientsAsync(backends.Sidecar, unknown, cancellationToken).ConfigureAwait(false))
            {
                Add(recipient);
            }
        }

        return new MailboxGrants(
            table.Entries.Select(entry => new MailboxGrant(entry.Right, entry.Mailbox, entry.Holder, byName.GetValueOrDefault(entry.Holder))).ToList(),
            table.Failed);
    }

    /// <summary>
    /// The grants the user holds through a group: an entry whose holder is one of the user's groups by its name, its
    /// address, its alias or its object id, and for a holder no name matched (and no mailbox is), the object id one
    /// piped <c>Get-EXORecipient</c> resolves it to.
    /// </summary>
    private static async Task<IReadOnlyList<UserGrant>> GroupGrantsAsync(
        IExoCmdletRunner exo,
        IReadOnlyList<UserGroup> groups,
        List<(MailboxRight Right, string Holder, MailboxInfo Mailbox)> others,
        Dictionary<string, MailboxInfo> byKey,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0 || others.Count == 0)
        {
            return [];
        }

        var byName = new Dictionary<string, MemberGroup>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, MemberGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (UserGroup group in groups)
        {
            var via = new MemberGroup(group.Id, group.DisplayName);
            byId.TryAdd(group.Id, via);
            foreach (string? key in new[] { group.Id, group.DisplayName, group.Mail, group.MailNickname })
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    byName.TryAdd(key, via);
                }
            }
        }

        List<string> unknown = others
            .Select(entry => entry.Holder)
            .Where(holder => !byName.ContainsKey(holder) && !byKey.ContainsKey(holder) && !holder.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
        {
            foreach (ExoObject recipient in await RecipientsAsync(exo, unknown, cancellationToken).ConfigureAwait(false))
            {
                if (recipient.Text("ExternalDirectoryObjectId") is { } id && byId.TryGetValue(id, out MemberGroup? via))
                {
                    foreach (string key in Keys(recipient))
                    {
                        byName.TryAdd(key, via);
                    }
                }
            }
        }

        return others
            .Where(entry => byName.ContainsKey(entry.Holder))
            .Select(entry => new UserGrant(entry.Right, entry.Mailbox) { Via = byName[entry.Holder] })
            .ToList();
    }

    /// <summary>The FullAccess entries of every mailbox, read the way the options name (task 043).</summary>
    private static Task<ExoResult> FullAccessAsync(IExoCmdletRunner exo, ExchangeBackends? backends, IReadOnlyList<ExoObject> mailboxes, UserAccessOptions options, CancellationToken cancellationToken)
    {
        var command = new ExoCommand(ExoCmdlets.GetMailboxPermission) { Input = Identities(mailboxes), Properties = PermissionProperties };
        return options.Method == PermissionReadMethod.Parallel
            ? SplitAsync(exo, backends, command, options.Sidecars, cancellationToken)
            : exo.InvokeAsync(command, cancellationToken);
    }

    /// <summary>
    /// A piped cmdlet run split across several sidecars at once (task 043): the first share on the command's own
    /// sidecar, every other one on a sidecar started for it (each connects with the same token); the objects and the
    /// errors of every share together. One sidecar when the run cannot start more.
    /// </summary>
    private static async Task<ExoResult> SplitAsync(IExoCmdletRunner exo, ExchangeBackends? backends, ExoCommand command, int sidecars, CancellationToken cancellationToken)
    {
        // Every command split here pipes its input (the mailboxes' identities).
        IReadOnlyList<string> input = command.Input!;
        if (backends is null || !backends.CanStartSidecars || input.Count < 2 || sidecars < 2)
        {
            return await exo.InvokeAsync(command, cancellationToken).ConfigureAwait(false);
        }

        int shares = Math.Min(sidecars, input.Count);
        int size = (input.Count + shares - 1) / shares;
        List<Task<ExoResult>> runs = input
            .Chunk(size)
            .Select((share, index) => (index == 0 ? exo : backends.StartSidecar()).InvokeAsync(command with { Input = share }, cancellationToken))
            .ToList();
        ExoResult[] results = await Task.WhenAll(runs).ConfigureAwait(false);
        return new ExoResult(results.SelectMany(result => result.Items).ToList(), results.SelectMany(result => result.Errors).ToList());
    }

    /// <summary>The live sizes, over the sidecar alone (<paramref name="backends"/> null) or over the layers.</summary>
    private static async Task<CommandResult<MailboxSizes>> SizesAsync(IExoCmdletRunner exo, ExchangeBackends? backends, string? mailbox, int? top, MailboxSizeOptions options, CancellationToken cancellationToken)
    {
        RequireTop(top);
        RequireSidecars(options.Sidecars);
        string[] extra = ["ExchangeGuid"];
        IReadOnlyList<ExoObject> mailboxes = string.IsNullOrWhiteSpace(mailbox)
            ? await AllMailboxesAsync(exo, sharedOnly: false, extra, cancellationToken).ConfigureAwait(false)
            : [await OneMailboxAsync(exo, mailbox.Trim(), extra, cancellationToken).ConfigureAwait(false)];

        var failed = new List<string>();
        var sizes = new List<MailboxSize>();
        if (mailboxes.Count > 0)
        {
            var command = new ExoCommand(ExoCmdlets.GetMailboxStatistics)
            {
                Input = Identities(mailboxes),
                Properties = ["DisplayName", "MailboxGuid", "ItemCount", "TotalItemSize", "DeletedItemCount", "TotalDeletedItemSize"],
            };
            ExoResult statistics = options.Method == MailboxSizeMethod.Parallel
                ? await SplitAsync(exo, backends, command, options.Sidecars, cancellationToken).ConfigureAwait(false)
                : await exo.InvokeAsync(command, cancellationToken).ConfigureAwait(false);
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

        var result = new MailboxSizes(Largest(sizes, top), mailboxes.Count, failed)
        {
            Method = backends is null ? null : options.Method == MailboxSizeMethod.Parallel ? "parallel" : "piped",
        };
        return failed.Count == 0
            ? CommandResult.Success(result)
            : CommandResult.Failure(Partial(failed.Count, "mailbox size(s)"), result);
    }

    /// <summary>The usage report's period: the shortest, so its sizes are the latest the report has.</summary>
    internal const string ReportPath = "/reports/getMailboxUsageDetail(period='D7')";

    /// <summary>
    /// The sizes from Graph's mailbox usage report (<c>getMailboxUsageDetail</c>, a CSV behind a redirect; task 043)
    /// joined by user principal name to the mailbox list of the Admin API (the sidecar's when the run has none), both
    /// read side by side. A report whose names are concealed (the tenant's privacy setting for the reports) cannot
    /// be joined and fails with the setting in the hint.
    /// </summary>
    private static async Task<CommandResult<MailboxSizes>> ReportSizesAsync(ExchangeBackends backends, string? mailbox, int? top, CancellationToken cancellationToken)
    {
        RequireTop(top);
        if (!backends.HasGraph)
        {
            throw new InvalidOperationException("the usage report needs the Graph client");
        }

        // The two reads run side by side; each is timed apart from the other (task 048), so the log says which one
        // the command waits for.
        GraphRequest request = GraphRequest.Get(ReportPath) with { Permission = "Reports.Read.All", Accept = "*/*" };
        var clock = Stopwatch.StartNew();
        Task<GraphResponse> report = TimedAsync(backends.Graph.SendAsync(request, cancellationToken), clock, "Graph's usage report");
        Task<IReadOnlyList<ExoObject>> list = TimedAsync(
            string.IsNullOrWhiteSpace(mailbox)
                ? ListForReportAsync(backends, cancellationToken)
                : OneForReportAsync(backends, mailbox.Trim(), cancellationToken),
            clock,
            backends.HasAdminApi ? "the Admin API's mailbox list" : "the sidecar's mailbox list");
        try
        {
            await Task.WhenAll(report, list).ConfigureAwait(false);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.Forbidden && report.IsFaulted)
        {
            // The report is the default for every mailbox: the hint names the way around a missing permission too.
            throw new AegisException(
                FailureKind.Forbidden,
                exception.Message,
                $"{exception.Failure.Hint}; without it, `--method parallel` reads the live sizes",
                exception);
        }

        IReadOnlyList<UsageRow> rows = UsageReport((await report.ConfigureAwait(false)).Body);
        IReadOnlyList<ExoObject> mailboxes = await list.ConfigureAwait(false);
        if (rows.Count > 0 && rows.All(row => ConcealedName().IsMatch(row.UserPrincipalName)))
        {
            throw new AegisException(
                FailureKind.ServiceError,
                "the mailbox usage report conceals the user names, so its sizes cannot be told apart",
                "in the Microsoft 365 admin center, Settings > Org settings > Reports, turn off concealed names; or read the live sizes with --method parallel");
        }

        var byName = new Dictionary<string, UsageRow>(StringComparer.OrdinalIgnoreCase);
        foreach (UsageRow row in rows.Where(row => !row.Deleted))
        {
            byName.TryAdd(row.UserPrincipalName, row);
        }

        var sizes = new List<MailboxSize>();
        int missing = 0;
        foreach (ExoObject item in mailboxes)
        {
            MailboxInfo info = ToMailbox(item);
            UsageRow? row = (info.UserPrincipalName is { } upn && byName.TryGetValue(upn, out UsageRow? byUpn)) ? byUpn
                : byName.TryGetValue(info.PrimarySmtpAddress, out UsageRow? byAddress) ? byAddress
                : null;
            if (row is null)
            {
                missing++;
                continue;
            }

            sizes.Add(new MailboxSize(info, row.ItemCount, row.StorageBytes, null, row.DeletedItemCount, row.DeletedItemBytes));
        }

        DateOnly? day = rows.Select(row => row.RefreshDate).Where(date => date is not null).Max();
        return CommandResult.Success(new MailboxSizes(Largest(sizes, top), mailboxes.Count, []) { Method = "report", ReportDate = day, NotInReport = missing });
    }

    private static async Task<IReadOnlyList<ExoObject>> ListForReportAsync(ExchangeBackends backends, CancellationToken cancellationToken)
    {
        if (backends.HasAdminApi)
        {
            return await AdminMailboxesAsync(backends.AdminApi!, cancellationToken).ConfigureAwait(false);
        }

        return await AllMailboxesAsync(backends.Sidecar, sharedOnly: false, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every mailbox from the Admin API's <c>Mailbox</c> endpoint, over every page.</summary>
    private static async Task<IReadOnlyList<ExoObject>> AdminMailboxesAsync(IExoCmdletRunner admin, CancellationToken cancellationToken)
    {
        // $select keeps the page to the fields the list shows: the preview answers the whole Get-Mailbox otherwise.
        ExoResult result = await admin.InvokeAsync(
            new ExoCommand(AdminApiCmdlets.GetMailbox) { Parameters = new Dictionary<string, object?> { ["ResultSize"] = Unlimited }, Properties = MailboxProperties },
            cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>A read that writes, under <c>--verbose</c>, how long it took from the start of <paramref name="clock"/>.</summary>
    private static async Task<T> TimedAsync<T>(Task<T> read, Stopwatch clock, string what)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        finally
        {
            LogSink.Verbose?.Write("exchange-online", string.Create(
                CultureInfo.InvariantCulture,
                $"mailbox size, report method: {what} {(read.IsCompletedSuccessfully ? "answered" : "failed")} after {clock.ElapsedMilliseconds} ms"));
        }
    }

    private static async Task<IReadOnlyList<ExoObject>> OneForReportAsync(ExchangeBackends backends, string identity, CancellationToken cancellationToken) =>
        backends.HasAdminApi
            ? [await AdminMailboxAsync(backends.AdminApi!, identity, displayNames: false, cancellationToken).ConfigureAwait(false)]
            : [await OneMailboxAsync(backends.Sidecar, identity, [], cancellationToken).ConfigureAwait(false)];

    /// <summary>One row of the mailbox usage report.</summary>
    internal sealed record UsageRow(string UserPrincipalName, bool Deleted, long ItemCount, long? StorageBytes, long DeletedItemCount, long? DeletedItemBytes, DateOnly? RefreshDate);

    /// <summary>The rows of the usage report's CSV (a header line, then a row per mailbox; quoted fields allowed).</summary>
    internal static IReadOnlyList<UsageRow> UsageReport(string csv)
    {
        List<List<string>> lines = Csv(csv);
        if (lines.Count == 0)
        {
            return [];
        }

        List<string> header = lines[0];
        int Column(string name) => header.FindIndex(column => string.Equals(column.Trim(), name, StringComparison.OrdinalIgnoreCase));
        int upn = Column("User Principal Name");
        int deleted = Column("Is Deleted");
        int items = Column("Item Count");
        int storage = Column("Storage Used (Byte)");
        int deletedItems = Column("Deleted Item Count");
        int deletedBytes = Column("Deleted Item Size (Byte)");
        int refresh = Column("Report Refresh Date");
        if (upn < 0)
        {
            throw new AegisException(FailureKind.ServiceError, "the mailbox usage report has no User Principal Name column", "run the command again; if it persists, report it");
        }

        string Field(List<string> line, int index) => index >= 0 && index < line.Count ? line[index].Trim() : "";
        long? Long(List<string> line, int index) => long.TryParse(Field(line, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;
        return lines.Skip(1)
            .Where(line => Field(line, upn).Length > 0)
            .Select(line => new UsageRow(
                Field(line, upn),
                string.Equals(Field(line, deleted), "True", StringComparison.OrdinalIgnoreCase),
                Long(line, items) ?? 0,
                Long(line, storage),
                Long(line, deletedItems) ?? 0,
                Long(line, deletedBytes),
                DateOnly.TryParse(Field(line, refresh), CultureInfo.InvariantCulture, out DateOnly day) ? day : null))
            .ToList();
    }

    /// <summary>The records of a CSV text: commas between fields, a field in quotes may hold commas, quotes ("") and line breaks.</summary>
    private static List<List<string>> Csv(string text)
    {
        var lines = new List<List<string>>();
        var line = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        string body = text.TrimStart('﻿');
        for (int index = 0; index < body.Length; index++)
        {
            char character = body[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < body.Length && body[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    line.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    line.Add(field.ToString());
                    field.Clear();
                    lines.Add(line);
                    line = [];
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || line.Count > 0)
        {
            line.Add(field.ToString());
            lines.Add(line);
        }

        return lines.Where(record => record.Any(value => value.Length > 0)).ToList();
    }

    /// <summary>A concealed name in a usage report: 32 hex digits in place of the user principal name.</summary>
    [GeneratedRegex("^[0-9A-Fa-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ConcealedName();

    private static List<MailboxSize> Largest(IEnumerable<MailboxSize> sizes, int? top)
    {
        IEnumerable<MailboxSize> ordered = sizes
            .OrderByDescending(size => size.TotalItemBytes ?? -1)
            .ThenBy(size => size.Mailbox.DisplayName, StringComparer.OrdinalIgnoreCase);
        return (top is { } limit ? ordered.Take(limit) : ordered).ToList();
    }

    /// <summary>The identities a piped cmdlet reads the mailboxes by: the object id, else the address.</summary>
    private static List<string> Identities(IReadOnlyList<ExoObject> mailboxes) =>
        mailboxes.Select(mailbox => mailbox.Text("ExternalDirectoryObjectId") ?? mailbox.Text("PrimarySmtpAddress") ?? "").Where(key => key.Length > 0).ToList();

    /// <summary>A value that names someone, as text: a string, or an object's display name, name or address.</summary>
    private static string? Name(ExoObject item, string property)
    {
        if (item.Text(property) is { } text)
        {
            return text;
        }

        if (item.Value(property) is { ValueKind: JsonValueKind.Object } value)
        {
            foreach (string candidate in new[] { "DisplayName", "Name", "PrimarySmtpAddress", "UserPrincipalName", "Identity" })
            {
                if (value.TryGetProperty(candidate, out JsonElement inner) && inner.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(inner.GetString()))
                {
                    return inner.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>A list value's words: a list, or a text of comma-separated words; <c>None</c> left out unless kept.</summary>
    private static List<string> Words(ExoObject item, string property, bool keepNone) =>
        item.Texts(property)
            .SelectMany(text => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(word => keepNone || !string.Equals(word, "None", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Method(PermissionReadMethod method) => method == PermissionReadMethod.Parallel ? "parallel" : "piped";

    private static IExoCmdletRunner RequireAdminApi(ExchangeBackends backends) =>
        backends.AdminApi ?? throw new AegisException(
            FailureKind.ServiceError,
            "this read needs the Exchange Online Admin API, which this run does not have",
            "the offline tests name the recorded answers with M365AEGIS_GRAPH_FIXTURES");

    private static void RequireTop(int? top)
    {
        if (top is <= 0)
        {
            throw new AegisException(FailureKind.Usage, $"--top must be 1 or more, not {top}", "leave --top out to list every mailbox");
        }
    }

    private static void RequireSidecars(int sidecars)
    {
        if (sidecars is < 2 or > 8)
        {
            throw new AegisException(FailureKind.Usage, $"--sidecars must be 2 to 8, not {sidecars}", "the parallel read starts that many pwsh processes; 4 is the default");
        }
    }

    private static AegisException MailboxNotFound(string identity, Exception? inner) => inner is null
        ? new(FailureKind.NotFound, $"mailbox '{identity}' not found", SidecarFailures.NotFoundHint)
        : new(FailureKind.NotFound, $"mailbox '{identity}' not found", SidecarFailures.NotFoundHint, inner);

    /// <summary>
    /// A faulted read's failure. Only the contract's failure gets here: the side-by-side read above catches an
    /// <see cref="AegisException"/> only, so any other exception (a bug) has already left the method as it was.
    /// </summary>
    private static AegisException Unwrap(Task task) => (AegisException)task.Exception!.InnerException!;
}
