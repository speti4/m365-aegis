using System.Globalization;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>One mailbox right a write was asked to grant or revoke.</summary>
/// <param name="Mailbox">The mailbox as given: its address, name or object id.</param>
/// <param name="User">The holder as given: a user principal name, an address, a group's name or address.</param>
/// <param name="Right">The right.</param>
/// <param name="Row">The CSV line it comes from (task 054), or null for the arguments.</param>
/// <param name="AutoMapping">
/// For a FullAccess grant, whether Outlook adds the mailbox for the holder on its own (task 059: false is
/// <c>Add-MailboxPermission -AutoMapping:$false</c>); true, the service's default, for every other right.
/// </param>
public sealed record AccessChangeRow(string Mailbox, string User, MailboxRight Right, int? Row = null, bool AutoMapping = true);

/// <summary>One mailbox's forwarding a write was asked to set or clear.</summary>
/// <param name="Mailbox">The mailbox as given.</param>
/// <param name="To">The address to forward to; null clears the forwarding.</param>
/// <param name="KeepCopy">Whether the mailbox keeps a copy (<c>DeliverToMailboxAndForward</c>); ignored when clearing.</param>
/// <param name="Row">The CSV line it comes from, or null for the arguments.</param>
public sealed record ForwardingRow(string Mailbox, string? To, bool KeepCopy, int? Row = null);

/// <summary>
/// The mailbox writes (task 053), beside the lookups whose reads give their current state: <c>mailbox access
/// grant|revoke</c> and <c>mailbox forwarding set|clear</c>, each a <see cref="WritePlan"/>. The plan reads first —
/// the holders as <c>mailbox access</c> reads them (the Admin API and the sidecar, task 042), each holder to change as
/// a recipient (<c>Get-EXORecipient</c>, so a grant held already is told by every name Exchange may use for it), the
/// forwarding as <c>Get-EXOMailbox</c> carries it and the accepted domains — and lists only what is not so already.
/// The apply goes through the sidecar alone, a cmdlet per change: <c>Add-/Remove-MailboxPermission</c> (FullAccess),
/// <c>Add-/Remove-RecipientPermission</c> (Send As), <c>Set-Mailbox -GrantSendOnBehalfTo @{Add=…}</c> or
/// <c>@{Remove=…}</c> (Send on Behalf), <c>Set-Mailbox -ForwardingSmtpAddress … -DeliverToMailboxAndForward …</c>; the
/// Admin API has no documented write (D-004). A mailbox or a holder that does not exist fails the whole plan before
/// anything is sent.
/// </summary>
public static partial class MailboxLookups
{
    private static string[] HolderProperties => [.. RecipientProperties, "UserPrincipalName"];
    private static readonly string[] ForwardingProperties = ["ForwardingSmtpAddress", "ForwardingAddress", "DeliverToMailboxAndForward"];

    /// <summary>The columns of a <c>--from-csv</c> file of <c>mailbox access grant|revoke</c>.</summary>
    public static IReadOnlyList<string> AccessCsvColumns { get; } = ["mailbox", "user", "right"];

    /// <summary>The columns of a <c>--from-csv</c> file of <c>mailbox forwarding set</c> (<c>keepCopy</c> is optional).</summary>
    public static IReadOnlyList<string> ForwardingCsvColumns { get; } = ["mailbox", "to"];

    /// <summary>The optional column of <c>mailbox forwarding set</c>'s file.</summary>
    public const string KeepCopyColumn = "keepCopy";

    /// <summary>The optional column of <c>mailbox access grant</c>'s file (task 059): <c>false</c> on a FullAccess line grants it without automapping.</summary>
    public const string AutoMappingColumn = "automapping";

    /// <summary>The right a word names: <c>FullAccess</c>, <c>SendAs</c>, <c>SendOnBehalf</c>, in any case, with or without hyphens.</summary>
    public static MailboxRight? ParseRight(string? text)
    {
        string word = (text ?? "").Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        return word.ToUpperInvariant() switch
        {
            "FULLACCESS" => MailboxRight.FullAccess,
            "SENDAS" => MailboxRight.SendAs,
            "SENDONBEHALF" or "SENDONBEHALFOF" => MailboxRight.SendOnBehalf,
            _ => null,
        };
    }

    /// <summary>
    /// The rows of the arguments: one mailbox, each user with each right; with <paramref name="autoMapping"/> false
    /// (<c>--no-automapping</c>, task 059) the FullAccess rows are granted without automapping.
    /// </summary>
    /// <exception cref="AegisException">A usage error: no mailbox, no user, no right or an unknown one, or no automapping without FullAccess.</exception>
    public static IReadOnlyList<AccessChangeRow> AccessRows(string? mailbox, IReadOnlyList<string>? users, IReadOnlyList<string>? rights, bool autoMapping = true)
    {
        string identity = RequireName(mailbox, "the mailbox");
        List<string> holders = (users ?? []).Where(user => !string.IsNullOrWhiteSpace(user)).Select(user => user.Trim()).ToList();
        if (holders.Count == 0)
        {
            throw new AegisException(FailureKind.Usage, "no --user was named", "give --user with a user principal name, an address or a group's address");
        }

        List<string> words = (rights ?? []).SelectMany(right => right.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        if (words.Count == 0)
        {
            throw new AegisException(FailureKind.Usage, "no --right was named", "give --right FullAccess, SendAs or SendOnBehalf (several with commas)");
        }

        var parsed = new List<MailboxRight>();
        foreach (string word in words)
        {
            MailboxRight right = ParseRight(word) ?? throw new AegisException(FailureKind.Usage, $"unknown right '{word}'", "give FullAccess, SendAs or SendOnBehalf");
            if (!parsed.Contains(right))
            {
                parsed.Add(right);
            }
        }

        if (!autoMapping && !parsed.Contains(MailboxRight.FullAccess))
        {
            throw new AegisException(
                FailureKind.Usage,
                $"--no-automapping goes with --right FullAccess, not {string.Join(" or ", parsed.Select(RightName))}",
                "automapping is how Outlook adds a mailbox its FullAccess holder can open; drop --no-automapping, or grant FullAccess");
        }

        return holders.SelectMany(user => parsed.Select(right => new AccessChangeRow(identity, user, right, AutoMapping: autoMapping || right != MailboxRight.FullAccess))).ToList();
    }

    /// <summary>
    /// The rows of a CSV file with the columns <c>mailbox,user,right</c> (task 054) and, for a grant, optionally
    /// <c>automapping</c> (task 059: <c>false</c> on a FullAccess line; empty is true).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="grant">Whether the file grants (the <c>automapping</c> column is a grant's only).</param>
    /// <exception cref="AegisException">A usage error naming the file and the line.</exception>
    public static IReadOnlyList<AccessChangeRow> AccessRowsFromCsv(string path, bool grant = true)
    {
        var rows = new List<AccessChangeRow>();
        foreach (CsvRow row in CsvRows.Read(path, AccessCsvColumns, grant ? [AutoMappingColumn] : []))
        {
            string where = string.Format(CultureInfo.InvariantCulture, "{0}, line {1}", Path.GetFileName(path), row.Line);
            MailboxRight right = ParseRight(row["right"]) ?? throw new AegisException(
                FailureKind.Usage,
                $"{where}: unknown right '{row["right"]}'",
                "the right is FullAccess, SendAs or SendOnBehalf; nothing was sent");
            bool autoMapping = row.Flag(AutoMappingColumn, where, whenEmpty: true);
            if (!autoMapping && right != MailboxRight.FullAccess)
            {
                throw new AegisException(
                    FailureKind.Usage,
                    $"{where}: automapping is false on a {RightName(right)} line",
                    "automapping is a FullAccess setting; leave the cell empty on the other lines; nothing was sent");
            }

            rows.Add(new AccessChangeRow(row["mailbox"]!, row["user"]!, right, row.Line, autoMapping));
        }

        return rows;
    }

    /// <summary>The rows of a CSV file of <c>mailbox forwarding set</c> (<c>mailbox,to[,keepCopy]</c>) or <c>clear</c> (<c>mailbox</c>).</summary>
    /// <exception cref="AegisException">A usage error naming the file and the line.</exception>
    public static IReadOnlyList<ForwardingRow> ForwardingRowsFromCsv(string path, bool set)
    {
        string name = Path.GetFileName(path);
        IReadOnlyList<CsvRow> rows = set ? CsvRows.Read(path, ForwardingCsvColumns, [KeepCopyColumn]) : CsvRows.Read(path, ["mailbox"], []);
        var result = new List<ForwardingRow>();
        foreach (CsvRow row in rows)
        {
            string where = string.Format(CultureInfo.InvariantCulture, "{0}, line {1}", name, row.Line);
            if (!set)
            {
                result.Add(new ForwardingRow(row["mailbox"]!, null, false, row.Line));
                continue;
            }

            string to = RequireAddress(row["to"], where);
            bool keep = row.Flag(KeepCopyColumn, where, whenEmpty: false);
            result.Add(new ForwardingRow(row["mailbox"]!, to, keep, row.Line));
        }

        return Distinct(result, name);
    }

    /// <summary>The row of the arguments of <c>mailbox forwarding set|clear</c>.</summary>
    /// <exception cref="AegisException">A usage error: no mailbox, or an address that is none.</exception>
    public static IReadOnlyList<ForwardingRow> ForwardingRows(string? mailbox, string? to, bool keepCopy, bool set)
    {
        string identity = RequireName(mailbox, "the mailbox");
        return set ? [new ForwardingRow(identity, RequireAddress(to, "--to"), keepCopy)] : [new ForwardingRow(identity, null, false)];
    }

    /// <summary>The plan of <c>mailbox access grant|revoke</c> over the rows.</summary>
    /// <param name="backends">The layers: the reads through the Admin API and the sidecar, the apply through the sidecar.</param>
    /// <param name="grant">Grant the rights, or revoke them.</param>
    /// <param name="rows">What to change.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="AegisException">A mailbox or a holder that does not exist, or a failed read; nothing was sent.</exception>
    public static async Task<WritePlan> PlanAccessAsync(ExchangeBackends backends, bool grant, IReadOnlyList<AccessChangeRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new AegisException(FailureKind.Usage, "no change was asked for", "give a mailbox with --user and --right, or --from-csv");
        }

        var holders = new Dictionary<string, ExoObject>(StringComparer.OrdinalIgnoreCase);
        foreach (AccessChangeRow row in rows.DistinctBy(row => row.User, StringComparer.OrdinalIgnoreCase))
        {
            holders[row.User] = await RecipientAsync(backends.Sidecar, row.User, row.Row, cancellationToken).ConfigureAwait(false);
        }

        var accesses = new Dictionary<string, MailboxAccess>(StringComparer.OrdinalIgnoreCase);
        foreach (AccessChangeRow row in rows.DistinctBy(row => row.Mailbox, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                accesses[row.Mailbox] = (await AccessAsync(backends, row.Mailbox, new MailboxAccessOptions(), cancellationToken).ConfigureAwait(false)).Data!;
            }
            catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound && row.Row is { } line)
            {
                throw new AegisException(FailureKind.NotFound, string.Format(CultureInfo.InvariantCulture, "{0} (line {1})", exception.Failure.Message, line), exception.Failure.Hint, exception);
            }
        }

        var builder = new PlanBuilder(grant ? "mailbox-access-grant" : "mailbox-access-revoke", "");
        var planned = new HashSet<(string Mailbox, string Holder, MailboxRight Right)>();
        var granted = new List<(ExoObject Holder, string Who, MailboxInfo Mailbox, List<MailboxRight> Rights)>();
        foreach (AccessChangeRow row in rows)
        {
            MailboxAccess access = accesses[row.Mailbox];
            ExoObject holder = holders[row.User];
            HashSet<string> names = Keys(holder);
            names.Add(row.User);
            string right = RightName(row.Right);
            string target = $"{access.Mailbox.DisplayName} ({access.Mailbox.PrimarySmtpAddress})";
            string who = Describe(holder, row.User);
            bool withoutAutoMapping = grant && row.Right == MailboxRight.FullAccess && !row.AutoMapping;
            string detail = withoutAutoMapping ? $"{right} for {who}, without automapping" : $"{right} for {who}";
            bool held = access.Holders.Any(entry => entry.Right == row.Right && (names.Contains(entry.Holder) || (entry.Address is not null && names.Contains(entry.Address))));
            string holds = $"holds {right}";
            string lacks = $"no {right}";
            if (held == grant)
            {
                // Automapping is set by the grant: a holder's own stays as it is (task 059).
                string already = withoutAutoMapping ? $"{holds} already; its automapping is left as it is" : $"{holds} already";
                builder.Unchanged(new PlanUnchanged(target, detail, grant ? already : lacks) { Row = row.Row });
                continue;
            }

            string key = holder.Text("PrimarySmtpAddress") ?? holder.Text("ExternalDirectoryObjectId") ?? row.User;
            if (!planned.Add((access.Mailbox.PrimarySmtpAddress.ToUpperInvariant(), key.ToUpperInvariant(), row.Right)))
            {
                builder.Unchanged(new PlanUnchanged(target, detail, "named twice: the change is planned once") { Row = row.Row });
                continue;
            }

            ExoCommand command = AccessCommand(grant, row.Right, access.Mailbox.PrimarySmtpAddress, key, autoMapping: !withoutAutoMapping);
            IExoCmdletRunner sidecar = backends.Sidecar;
            builder.Change(
                new PlanChange(grant ? PlanAction.Add : PlanAction.Remove, target, detail, grant ? lacks : holds, grant ? holds : lacks) { Row = row.Row },
                async ct => await sidecar.InvokeAsync(command, ct).ConfigureAwait(false));
            if (grant)
            {
                int index = granted.FindIndex(entry => ReferenceEquals(entry.Holder, holder) && entry.Mailbox == access.Mailbox);
                if (index < 0)
                {
                    granted.Add((holder, who, access.Mailbox, [row.Right]));
                }
                else if (!granted[index].Rights.Contains(row.Right))
                {
                    granted[index].Rights.Add(row.Right);
                }
            }
        }

        foreach ((ExoObject holder, string who, MailboxInfo mailbox, List<MailboxRight> rights) in granted)
        {
            WarnHolder(builder, holder, who, rights, mailbox);
        }

        List<MailboxInfo> mailboxes = accesses.Values.Select(access => access.Mailbox).DistinctBy(mailbox => mailbox.PrimarySmtpAddress, StringComparer.OrdinalIgnoreCase).ToList();
        string verb = grant ? "Grant mailbox rights on" : "Revoke mailbox rights on";
        builder.Title = mailboxes.Count == 1
            ? $"{verb} {mailboxes[0].DisplayName} ({mailboxes[0].PrimarySmtpAddress})"
            : string.Format(CultureInfo.InvariantCulture, "{0} {1} mailboxes", verb, mailboxes.Count);
        return builder.Build();
    }

    /// <summary>The plan of <c>mailbox forwarding set|clear</c> over the rows (a row without an address clears).</summary>
    /// <exception cref="AegisException">A mailbox that does not exist, a mailbox named twice, or a failed read; nothing was sent.</exception>
    public static async Task<WritePlan> PlanForwardingAsync(ExchangeBackends backends, IReadOnlyList<ForwardingRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new AegisException(FailureKind.Usage, "no change was asked for", "give a mailbox, or --from-csv");
        }

        IExoCmdletRunner sidecar = backends.Sidecar;
        bool set = rows.Any(row => row.To is not null);
        List<string> domains = [];
        if (set)
        {
            ExoResult accepted = await sidecar.InvokeAsync(new ExoCommand(ExoCmdlets.GetAcceptedDomain) { Properties = ["DomainName"] }, cancellationToken).ConfigureAwait(false);
            domains = accepted.Items.Select(item => item.Text("DomainName")).OfType<string>().ToList();
        }

        var builder = new PlanBuilder(set ? "mailbox-forwarding-set" : "mailbox-forwarding-clear", "");
        var seen = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var mailboxes = new List<MailboxInfo>();
        foreach (ForwardingRow row in rows)
        {
            ExoObject found;
            try
            {
                found = await OneMailboxAsync(sidecar, RequireName(row.Mailbox, "the mailbox"), ForwardingProperties, cancellationToken).ConfigureAwait(false);
            }
            catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound && row.Row is { } line)
            {
                throw new AegisException(FailureKind.NotFound, string.Format(CultureInfo.InvariantCulture, "{0} (line {1})", exception.Failure.Message, line), exception.Failure.Hint, exception);
            }

            MailboxInfo mailbox = ToMailbox(found);
            if (seen.TryGetValue(mailbox.PrimarySmtpAddress, out int? first))
            {
                throw new AegisException(
                    FailureKind.Usage,
                    $"{mailbox.PrimarySmtpAddress} is named twice{Lines(first, row.Row)}",
                    "give each mailbox one line: its forwarding has one target; nothing was sent");
            }

            seen[mailbox.PrimarySmtpAddress] = row.Row;
            mailboxes.Add(mailbox);
            string target = $"{mailbox.DisplayName} ({mailbox.PrimarySmtpAddress})";
            string? smtp = found.Text("ForwardingSmtpAddress") is { } address ? WithoutPrefix(address) : null;
            string? recipient = found.Text("ForwardingAddress");
            bool keeps = found.Flag("DeliverToMailboxAndForward") ?? false;
            string current = Forwarding(smtp, recipient, keeps);
            if (row.To is null)
            {
                if (smtp is null && recipient is null)
                {
                    builder.Unchanged(new PlanUnchanged(target, "the forwarding", "no forwarding") { Row = row.Row });
                    continue;
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["Identity"] = mailbox.PrimarySmtpAddress,
                    ["ForwardingSmtpAddress"] = null,
                    ["ForwardingAddress"] = null,
                    ["DeliverToMailboxAndForward"] = false,
                    ["Confirm"] = false,
                };
                AddStep(builder, sidecar, new PlanChange(PlanAction.Clear, target, "the forwarding", current, "no forwarding") { Row = row.Row }, parameters);
                continue;
            }

            string desired = Forwarding(row.To, null, row.KeepCopy);
            if (recipient is null && string.Equals(smtp, row.To, StringComparison.OrdinalIgnoreCase) && keeps == row.KeepCopy)
            {
                builder.Unchanged(new PlanUnchanged(target, $"forwarding to {row.To}", $"{desired} already") { Row = row.Row });
                continue;
            }

            var setting = new Dictionary<string, object?>
            {
                ["Identity"] = mailbox.PrimarySmtpAddress,
                ["ForwardingSmtpAddress"] = row.To,
                ["DeliverToMailboxAndForward"] = row.KeepCopy,
                ["Confirm"] = false,
            };
            if (recipient is not null)
            {
                setting["ForwardingAddress"] = null;
                builder.Warn($"{mailbox.DisplayName} forwards to the recipient '{recipient}' (ForwardingAddress), which takes precedence over an SMTP address: the plan clears it");
            }

            AddStep(builder, sidecar, new PlanChange(PlanAction.Set, target, $"forwarding to {row.To}", current, desired) { Row = row.Row }, setting);
            if (IsExternal(row.To, domains))
            {
                builder.Warn($"{row.To} is outside the accepted domains: the mail leaves the organization, and the outbound spam filter policy blocks automatic forwarding to an external address unless the policy allows it");
            }

            if (!row.KeepCopy)
            {
                builder.Warn($"{mailbox.DisplayName} keeps no copy: the mail reaches {row.To} only (--keep-copy keeps one)");
            }
        }

        string verb = set ? "Set the forwarding of" : "Clear the forwarding of";
        builder.Title = mailboxes.Count == 1
            ? $"{verb} {mailboxes[0].DisplayName} ({mailboxes[0].PrimarySmtpAddress})"
            : string.Format(CultureInfo.InvariantCulture, "{0} {1} mailboxes", verb, mailboxes.Count);
        return builder.Build();
    }

    private static void AddStep(PlanBuilder builder, IExoCmdletRunner sidecar, PlanChange change, Dictionary<string, object?> parameters)
    {
        var command = new ExoCommand(ExoCmdlets.SetMailbox) { Parameters = parameters };
        builder.Change(change, async ct => await sidecar.InvokeAsync(command, ct).ConfigureAwait(false));
    }

    /// <summary>The cmdlet that grants or revokes one right of one holder on one mailbox.</summary>
    private static ExoCommand AccessCommand(bool grant, MailboxRight right, string mailbox, string holder, bool autoMapping) => right switch
    {
        MailboxRight.FullAccess => new ExoCommand(grant ? ExoCmdlets.AddMailboxPermission : ExoCmdlets.RemoveMailboxPermission)
        {
            Parameters = FullAccessParameters(mailbox, holder, autoMapping),
        },
        MailboxRight.SendAs => new ExoCommand(grant ? ExoCmdlets.AddRecipientPermission : ExoCmdlets.RemoveRecipientPermission)
        {
            Parameters = new Dictionary<string, object?>
            {
                ["Identity"] = mailbox,
                ["Trustee"] = holder,
                ["AccessRights"] = new[] { "SendAs" },
                ["Confirm"] = false,
            },
        },
        _ => new ExoCommand(ExoCmdlets.SetMailbox)
        {
            Parameters = new Dictionary<string, object?>
            {
                ["Identity"] = mailbox,
                ["GrantSendOnBehalfTo"] = new Dictionary<string, object?> { [grant ? "Add" : "Remove"] = new[] { holder } },
                ["Confirm"] = false,
            },
        },
    };

    /// <summary>
    /// The parameters of <c>Add-</c> or <c>Remove-MailboxPermission</c>: FullAccess on every folder, and
    /// <c>-AutoMapping:$false</c> on a grant without automapping (task 059; the service's default is true, so it is sent
    /// only when it is false).
    /// </summary>
    private static Dictionary<string, object?> FullAccessParameters(string mailbox, string holder, bool autoMapping)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["Identity"] = mailbox,
            ["User"] = holder,
            ["AccessRights"] = new[] { "FullAccess" },
            ["InheritanceType"] = "All",
        };
        if (!autoMapping)
        {
            parameters["AutoMapping"] = false;
        }

        parameters["Confirm"] = false;
        return parameters;
    }

    /// <summary>The warnings of a grant: a group holds it for every member; a recipient that is no security principal cannot hold it.</summary>
    private static void WarnHolder(PlanBuilder builder, ExoObject holder, string who, List<MailboxRight> rights, MailboxInfo mailbox)
    {
        string type = holder.Text("RecipientTypeDetails") ?? "";
        if (type is "MailContact" or "MailUniversalDistributionGroup" or "DynamicDistributionGroup" || type.Contains("DistributionGroup", StringComparison.OrdinalIgnoreCase))
        {
            builder.Warn($"{who} is a {type}: Exchange grants mailbox rights to mailboxes, mail users and mail-enabled security groups, so the service will likely refuse it");
        }
        else if (IsGroup(holder))
        {
            string automap = rights.Contains(MailboxRight.FullAccess) ? "; Outlook does not add the mailbox for them on its own (no automapping through a group)" : "";
            string named = rights.Count == 1
                ? RightName(rights[0])
                : $"{string.Join(", ", rights.Take(rights.Count - 1).Select(RightName))} and {RightName(rights[^1])}";
            builder.Warn($"{who} is a group: every member, the nested ones too, gets {named} on {mailbox.DisplayName}{automap}");
        }
    }

    /// <summary>A holder as a recipient, by the name given: a missing one fails the plan.</summary>
    private static async Task<ExoObject> RecipientAsync(IExoCmdletRunner sidecar, string user, int? row, CancellationToken cancellationToken)
    {
        string where = row is { } line ? string.Format(CultureInfo.InvariantCulture, " (line {0})", line) : "";
        try
        {
            ExoResult result = await sidecar.InvokeAsync(
                new ExoCommand(ExoCmdlets.GetRecipient) { Parameters = new Dictionary<string, object?> { ["Identity"] = user }, Properties = HolderProperties },
                cancellationToken).ConfigureAwait(false);
            if (result.Items.Count > 0)
            {
                return result.Items[0];
            }
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            throw new AegisException(FailureKind.NotFound, $"recipient '{user}' not found{where}", "give the holder's address or user principal name; nothing was sent", exception);
        }

        throw new AegisException(FailureKind.NotFound, $"recipient '{user}' not found{where}", "give the holder's address or user principal name; nothing was sent");
    }

    private static string Describe(ExoObject holder, string given)
    {
        string name = holder.Text("DisplayName") ?? holder.Text("Name") ?? given;
        string? address = holder.Text("PrimarySmtpAddress");
        return address is null || string.Equals(name, address, StringComparison.OrdinalIgnoreCase) ? name : $"{name} ({address})";
    }

    private static string RightName(MailboxRight right) => right switch
    {
        MailboxRight.FullAccess => "FullAccess",
        MailboxRight.SendAs => "Send As",
        _ => "Send on Behalf",
    };

    private static string Forwarding(string? smtp, string? recipient, bool keeps)
    {
        string copy = keeps ? "keeps a copy" : "keeps no copy";
        return recipient is not null ? $"forwards to the recipient '{recipient}', {copy}"
            : smtp is not null ? $"forwards to {smtp}, {copy}"
            : "no forwarding";
    }

    private static string RequireAddress(string? value, string what)
    {
        string text = (value ?? "").Trim();
        if (text.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..];
        }

        int at = text.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < text.Length - 1 && text.IndexOf('@', at + 1) < 0 && !text.Any(char.IsWhiteSpace)
            ? text
            : throw new AegisException(FailureKind.Usage, $"{what}: '{value}' is not an address", "give the address to forward to, such as bob@fabrikam.com; nothing was sent");
    }

    private static List<ForwardingRow> Distinct(List<ForwardingRow> rows, string file)
    {
        var lines = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (ForwardingRow row in rows)
        {
            if (lines.TryGetValue(row.Mailbox, out int? first))
            {
                throw new AegisException(FailureKind.Usage, $"{file}: {row.Mailbox} is named twice{Lines(first, row.Row)}", "give each mailbox one line: its forwarding has one target; nothing was sent");
            }

            lines[row.Mailbox] = row.Row;
        }

        return rows;
    }

    private static string Lines(int? first, int? second) =>
        first is { } one && second is { } two ? string.Format(CultureInfo.InvariantCulture, " (lines {0} and {1})", one, two) : "";
}
