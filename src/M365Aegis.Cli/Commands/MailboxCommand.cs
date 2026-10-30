using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis mailbox list|access|forwarders|size</c> (tasks 015 and 016) and
/// <c>mailbox folder-permissions|group-members|organization</c> (task 042): the mailbox lookups over the three layers of
/// D-004, each read through the layer that answers it best. <c>access</c> takes a mailbox (who holds which right on
/// it) or <c>--user</c> (every mailbox the user holds a right on), never both; <c>--expand-groups</c> reads a group's
/// members through Graph (task 044), and <c>--method</c> picks how the bulk reads go (task 043).
/// </summary>
internal static class MailboxCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command mailbox = new("mailbox", "Look up Exchange Online mailboxes: the list, who can open or send as them, forwarding, sizes.");

        Option<bool> shared = new("--shared") { Description = "Only the shared mailboxes." };
        Command list = new("list", "List the mailboxes.");
        list.Options.Add(shared);
        list.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            (ExchangeBackends backends, CancellationToken ct) => MailboxLookups.ListAsync(backends, parsed.GetValue(shared), ct),
            MailboxListTableView.Instance,
            cancellationToken));

        Argument<string?> accessOf = new("mailbox")
        {
            Description = "The mailbox: its address, name or object id.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<string?> user = new("--user") { Description = "The other direction: every mailbox this user (a user principal name) can open or send as." };
        Option<bool> expandGroups = new("--expand-groups")
        {
            Description = "Expand a grant held by a group into the group's members (through Graph); with --user, also the mailboxes the user reaches through a group.",
        };
        Option<string?> accessMethod = new("--method")
        {
            Description = "With --user, how every mailbox's FullAccess entries are read: parallel (split across sidecars, the default) or piped (one sidecar, one request per mailbox).",
        };
        Option<int> sidecars = new("--sidecars") { Description = "With --method parallel, how many sidecars run at once (2 to 8).", DefaultValueFactory = _ => 4 };
        Command access = new("access", "Show who holds FullAccess, Send As and Send on Behalf on a mailbox, or which mailboxes a user holds them on.");
        access.Arguments.Add(accessOf);
        access.Options.Add(user);
        access.Options.Add(expandGroups);
        access.Options.Add(accessMethod);
        access.Options.Add(sidecars);
        access.SetAction((parsed, cancellationToken) =>
        {
            OutputMode mode = Program.ResolveMode(parsed, json, table);
            string? target = parsed.GetValue(accessOf);
            string? holder = parsed.GetValue(user);
            if (string.IsNullOrWhiteSpace(target) == string.IsNullOrWhiteSpace(holder))
            {
                var failure = new AegisFailure(
                    FailureKind.Usage,
                    "give a mailbox or --user, not both and not neither",
                    "`mailbox access finance@contoso.com` lists who can use the mailbox; `mailbox access --user alice@contoso.com` lists what alice can use");
                return Task.FromResult(CommandRunner.Fail(mode, failure));
            }

            bool expand = parsed.GetValue(expandGroups);
            if (holder is null)
            {
                if (parsed.GetValue(accessMethod) is not null)
                {
                    return Task.FromResult(CommandRunner.Fail(mode, new AegisFailure(FailureKind.Usage, "--method goes with --user", "the access of one mailbox is a few calls whatever the tenant's size")));
                }

                return ExchangeRunner.RunAsync(parsed, json, table, profile, (backends, ct) => MailboxLookups.AccessAsync(backends, target!, new MailboxAccessOptions(expand), ct), MailboxAccessTableView.Instance, cancellationToken);
            }

            if (!TryParse(parsed.GetValue(accessMethod), PermissionReadMethods, out PermissionReadMethod method))
            {
                return Task.FromResult(CommandRunner.Fail(mode, new AegisFailure(FailureKind.Usage, $"unknown --method '{parsed.GetValue(accessMethod)}'", "give parallel or piped")));
            }

            var options = parsed.GetValue(accessMethod) is null
                ? new UserAccessOptions(expand, Sidecars: parsed.GetValue(sidecars))
                : new UserAccessOptions(expand, method, parsed.GetValue(sidecars));
            return ExchangeRunner.RunAsync(parsed, json, table, profile, (backends, ct) => MailboxLookups.AccessForUserAsync(backends, holder, options, ct), UserAccessTableView.Instance, cancellationToken);
        });

        Command forwarders = new("forwarders", "List the mailboxes that forward their mail, the external targets first.");
        forwarders.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            MailboxLookups.ForwardersAsync,
            ForwardersTableView.Instance,
            cancellationToken));

        Argument<string?> sizeOf = new("mailbox")
        {
            Description = "One mailbox; every mailbox when omitted.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<int?> top = new("--top") { Description = "Only the N largest mailboxes." };
        Option<string?> sizeMethod = new("--method")
        {
            Description = "How the sizes are read: report (Graph's mailbox usage report, a few days old, one call; the default for every mailbox), piped (live, one request per mailbox; the default for one mailbox) or parallel (live, split across sidecars).",
        };
        Option<int> sizeSidecars = new("--sidecars") { Description = "With --method parallel, how many sidecars run at once (2 to 8).", DefaultValueFactory = _ => 4 };
        Command size = new("size", "Show the size and item count of a mailbox, or of every mailbox, the largest first.");
        size.Arguments.Add(sizeOf);
        size.Options.Add(top);
        size.Options.Add(sizeMethod);
        size.Options.Add(sizeSidecars);
        size.SetAction((parsed, cancellationToken) =>
        {
            if (!TryParse(parsed.GetValue(sizeMethod), SizeMethods, out MailboxSizeMethod method))
            {
                return Task.FromResult(CommandRunner.Fail(
                    Program.ResolveMode(parsed, json, table),
                    new AegisFailure(FailureKind.Usage, $"unknown --method '{parsed.GetValue(sizeMethod)}'", "give piped, parallel or report")));
            }

            var options = new MailboxSizeOptions(parsed.GetValue(sizeMethod) is null ? null : method, parsed.GetValue(sizeSidecars));
            return ExchangeRunner.RunAsync(
                parsed, json, table, profile,
                (backends, ct) => MailboxLookups.SizeAsync(backends, parsed.GetValue(sizeOf), parsed.GetValue(top), options, ct),
                MailboxSizeTableView.Instance,
                cancellationToken);
        });

        Argument<string> folderOf = new("mailbox") { Description = "The mailbox: its address or name." };
        Option<string?> folder = new("--folder")
        {
            Description = "The folder, as the mailbox's own language names it (Calendar by default; Inbox/Reports for a nested one).",
        };
        Command folderPermissions = new("folder-permissions", "Show who holds which right on a folder of a mailbox, the calendar by default (through the Admin API).");
        folderPermissions.Arguments.Add(folderOf);
        folderPermissions.Options.Add(folder);
        folderPermissions.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            (backends, ct) => MailboxLookups.FolderPermissionsAsync(backends, parsed.GetValue(folderOf) ?? "", parsed.GetValue(folder), ct),
            FolderPermissionsTableView.Instance,
            cancellationToken));

        Argument<string> groupOf = new("group") { Description = "The distribution group: its address, name or GUID." };
        Command groupMembers = new("group-members", "List a distribution group's members as Exchange Online keeps them, hidden ones included (through the Admin API).");
        groupMembers.Arguments.Add(groupOf);
        groupMembers.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            (backends, ct) => MailboxLookups.DistributionGroupMembersAsync(backends, parsed.GetValue(groupOf) ?? "", ct),
            DistributionMembersTableView.Instance,
            cancellationToken));

        Command organization = new("organization", "Show the Exchange Online organization's MailTips settings and accepted domains (through the Admin API).");
        organization.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            MailboxLookups.OrganizationAsync,
            OrganizationTableView.Instance,
            cancellationToken));

        mailbox.Subcommands.Add(list);
        mailbox.Subcommands.Add(access);
        mailbox.Subcommands.Add(forwarders);
        mailbox.Subcommands.Add(size);
        mailbox.Subcommands.Add(folderPermissions);
        mailbox.Subcommands.Add(groupMembers);
        mailbox.Subcommands.Add(organization);
        return mailbox;
    }

    private static readonly Dictionary<string, PermissionReadMethod> PermissionReadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["piped"] = PermissionReadMethod.Piped,
        ["parallel"] = PermissionReadMethod.Parallel,
    };

    private static readonly Dictionary<string, MailboxSizeMethod> SizeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["piped"] = MailboxSizeMethod.Piped,
        ["parallel"] = MailboxSizeMethod.Parallel,
        ["report"] = MailboxSizeMethod.Report,
    };

    /// <summary>A <c>--method</c> value: the default when none was given, false for one not on the list.</summary>
    private static bool TryParse<T>(string? value, Dictionary<string, T> methods, out T method)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            method = default;
            return true;
        }

        return methods.TryGetValue(value.Trim(), out method);
    }

    /// <summary>The way a member came to hold a group's grant: the group, and the nested group it comes through.</summary>
    internal static string Through(string group, GroupMember member) =>
        member.Via is { Count: > 0 } via ? $"{group} > {string.Join(", ", via.Select(item => item.DisplayName))}" : group;

    /// <summary>A right as the tables write it.</summary>
    internal static string Right(MailboxRight right) => right switch
    {
        MailboxRight.FullAccess => "FullAccess",
        MailboxRight.SendAs => "Send As",
        MailboxRight.SendOnBehalf => "Send on Behalf",
        _ => "?",
    };

    /// <summary>A byte count as the tables write it: binary units, one decimal.</summary>
    internal static string Size(long? bytes)
    {
        if (bytes is not { } value)
        {
            return "?";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double scaled = value;
        int unit = 0;
        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value} B")
            : string.Create(CultureInfo.InvariantCulture, $"{scaled:0.0} {units[unit]}");
    }
}

/// <summary>The table of <c>mailbox list</c>: a row per mailbox, the count under the table.</summary>
internal sealed class MailboxListTableView : ITableView<MailboxList>
{
    internal static MailboxListTableView Instance { get; } = new();

    private MailboxListTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["name", "address", "type"];

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxList data) =>
        data.Mailboxes.Select(mailbox => (IReadOnlyList<string>)[mailbox.DisplayName, mailbox.PrimarySmtpAddress, mailbox.Type]);

    public IEnumerable<string> Footer(MailboxList data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} {1}mailbox(es)", data.Mailboxes.Count, data.SharedOnly ? "shared " : "");
    }
}

/// <summary>The table of <c>mailbox access &lt;mailbox&gt;</c>: a row per right and holder, the mailbox under the table.</summary>
internal sealed class MailboxAccessTableView : ITableView<MailboxAccess>
{
    internal static MailboxAccessTableView Instance { get; } = new();

    private MailboxAccessTableView()
    {
    }

    private static readonly string[] ExpandedHeaders = ["right", "holder", "address", "through"];

    public IReadOnlyList<string> Headers => ["right", "holder", "address"];

    public IReadOnlyList<string> HeadersFor(MailboxAccess data) => Expanded(data) ? ExpandedHeaders : Headers;

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxAccess data)
    {
        if (!Expanded(data))
        {
            return data.Holders.Select(holder => (IReadOnlyList<string>)[MailboxCommand.Right(holder.Right), holder.Holder, holder.Address ?? "-"]);
        }

        // A group's row, then a row per member under it: the right it holds through the group, and the way it came.
        return data.Holders.SelectMany(holder => new[]
            {
                (IReadOnlyList<string>)[MailboxCommand.Right(holder.Right), holder.Group == true ? $"{holder.Holder} (group)" : holder.Holder, holder.Address ?? "-", "-"],
            }
            .Concat((holder.Members ?? []).Select(member => (IReadOnlyList<string>)
                [MailboxCommand.Right(holder.Right), "  " + (member.DisplayName ?? member.Id), member.UserPrincipalName ?? member.Mail ?? "-", MailboxCommand.Through(holder.Holder, member)])));
    }

    public IEnumerable<string> Footer(MailboxAccess data)
    {
        if (!Expanded(data))
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "{0} grant(s) on {1} <{2}> ({3}); direct grants only, a group is not expanded",
                data.Holders.Count,
                data.Mailbox.DisplayName,
                data.Mailbox.PrimarySmtpAddress,
                data.Mailbox.Type);
            yield break;
        }

        int groups = data.Holders.Count(holder => holder.Group == true);
        int members = data.Holders.Sum(holder => holder.Members?.Count ?? 0);
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} grant(s) on {1} <{2}> ({3}); {4} held by a group, expanded into {5} member(s) over every nesting level",
            data.Holders.Count,
            data.Mailbox.DisplayName,
            data.Mailbox.PrimarySmtpAddress,
            data.Mailbox.Type,
            groups,
            members);
        int unexpanded = data.Holders.Count(holder => holder.Group == true && holder.Members is null);
        if (unexpanded > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} group(s) without a directory object (a dynamic distribution group) not expanded", unexpanded);
        }
    }

    private static bool Expanded(MailboxAccess data) => data.Holders.Any(holder => holder.Group is not null);
}

/// <summary>The table of <c>mailbox access --user</c>: a row per mailbox and right, what was read under the table.</summary>
internal sealed class UserAccessTableView : ITableView<UserAccess>
{
    internal static UserAccessTableView Instance { get; } = new();

    private UserAccessTableView()
    {
    }

    private static readonly string[] ExpandedHeaders = ["mailbox", "address", "type", "right", "through"];

    public IReadOnlyList<string> Headers => ["mailbox", "address", "type", "right"];

    public IReadOnlyList<string> HeadersFor(UserAccess data) => data.Groups is null ? Headers : ExpandedHeaders;

    public IEnumerable<IReadOnlyList<string>> Rows(UserAccess data) => data.Groups is null
        ? data.Grants.Select(grant => (IReadOnlyList<string>)
            [grant.Mailbox.DisplayName, grant.Mailbox.PrimarySmtpAddress, grant.Mailbox.Type, MailboxCommand.Right(grant.Right)])
        : data.Grants.Select(grant => (IReadOnlyList<string>)
            [grant.Mailbox.DisplayName, grant.Mailbox.PrimarySmtpAddress, grant.Mailbox.Type, MailboxCommand.Right(grant.Right), grant.Via?.DisplayName ?? "direct"]);

    public IEnumerable<string> Footer(UserAccess data)
    {
        if (data.Groups is { } groups)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "{0} grant(s) for {1}{2} over {3} mailbox(es), {4} of them through one of the user's {5} group(s)",
                data.Grants.Count,
                data.User,
                data.DisplayName is null ? "" : $" ({data.DisplayName})",
                data.MailboxesScanned,
                data.Grants.Count(grant => grant.Via is not null),
                groups);
        }
        else
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "{0} grant(s) for {1}{2} over {3} mailbox(es); direct grants only, a group's members are not expanded",
                data.Grants.Count,
                data.User,
                data.DisplayName is null ? "" : $" ({data.DisplayName})",
                data.MailboxesScanned);
        }


        if (data.Failed.Count > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} list(s) could not be read", data.Failed.Count);
        }
    }
}

/// <summary>The table of <c>mailbox forwarders</c>: a row per forwarding mailbox, the external ones first.</summary>
internal sealed class ForwardersTableView : ITableView<MailboxForwarders>
{
    internal static ForwardersTableView Instance { get; } = new();

    private ForwardersTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["mailbox", "address", "forwards to", "external", "keeps a copy"];

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxForwarders data) => data.Forwarders.Select(forwarder => (IReadOnlyList<string>)
    [
        forwarder.Mailbox.DisplayName,
        forwarder.Mailbox.PrimarySmtpAddress,
        forwarder.Kind == ForwardingKind.Recipient && forwarder.TargetAddress is not null && !string.Equals(forwarder.Target, forwarder.TargetAddress, StringComparison.OrdinalIgnoreCase)
            ? $"{forwarder.Target} <{forwarder.TargetAddress}>"
            : forwarder.TargetAddress ?? forwarder.Target,
        forwarder.External switch { true => "EXTERNAL", false => "no", null => "?" },
        forwarder.KeepsCopy ? "yes" : "no",
    ]);

    public IEnumerable<string> Footer(MailboxForwarders data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} forwarding mailbox(es) of {1}, {2} to an external address; accepted domains: {3}",
            data.Forwarders.Select(forwarder => forwarder.Mailbox.PrimarySmtpAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            data.MailboxesScanned,
            data.Forwarders.Count(forwarder => forwarder.External == true),
            string.Join(", ", data.AcceptedDomains));
    }
}

/// <summary>The table of <c>mailbox size</c>: a row per mailbox, the largest first.</summary>
internal sealed class MailboxSizeTableView : ITableView<MailboxSizes>
{
    internal static MailboxSizeTableView Instance { get; } = new();

    private MailboxSizeTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["mailbox", "address", "type", "size", "items", "deleted"];

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxSizes data) => data.Mailboxes.Select(size => (IReadOnlyList<string>)
    [
        size.Mailbox.DisplayName,
        size.Mailbox.PrimarySmtpAddress,
        size.Mailbox.Type,
        MailboxCommand.Size(size.TotalItemBytes),
        size.ItemCount.ToString(CultureInfo.InvariantCulture),
        MailboxCommand.Size(size.DeletedItemBytes),
    ]);

    public IEnumerable<string> Footer(MailboxSizes data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} of {1} mailbox(es), {2} in all",
            data.Mailboxes.Count,
            data.MailboxesScanned,
            MailboxCommand.Size(data.Mailboxes.Sum(size => size.TotalItemBytes ?? 0)));
        if (data.Method == "report")
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "sizes from the mailbox usage report of {0}; {1} mailbox(es) not in the report yet",
                data.ReportDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?",
                data.NotInReport ?? 0);
        }
        else if (data.Method == "parallel")
        {
            yield return "read live with --method parallel";
        }

        if (data.Failed.Count > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} mailbox(es) could not be read", data.Failed.Count);
        }
    }
}

/// <summary>The table of <c>mailbox folder-permissions</c>: a row per entry, the folder under the table.</summary>
internal sealed class FolderPermissionsTableView : ITableView<MailboxFolderPermissions>
{
    internal static FolderPermissionsTableView Instance { get; } = new();

    private FolderPermissionsTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["user", "access rights", "sharing flags"];

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxFolderPermissions data) => data.Permissions.Select(entry => (IReadOnlyList<string>)
    [
        entry.User,
        entry.AccessRights.Count == 0 ? "-" : string.Join(", ", entry.AccessRights),
        entry.SharingPermissionFlags.Count == 0 ? "-" : string.Join(", ", entry.SharingPermissionFlags),
    ]);

    public IEnumerable<string> Footer(MailboxFolderPermissions data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} entr(ies) on the folder {1}{2} of {3}; Default is everyone in the organization, Anonymous everyone outside it",
            data.Permissions.Count,
            data.Folder,
            data.FolderName is { } name && !string.Equals(name, data.Folder, StringComparison.OrdinalIgnoreCase) ? $" ({name})" : "",
            data.Mailbox);
    }
}

/// <summary>The table of <c>mailbox group-members</c>: a row per member, the group under the table.</summary>
internal sealed class DistributionMembersTableView : ITableView<DistributionGroupMembers>
{
    internal static DistributionMembersTableView Instance { get; } = new();

    private DistributionMembersTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["name", "address", "type", "hidden"];

    public IEnumerable<IReadOnlyList<string>> Rows(DistributionGroupMembers data) => data.Members.Select(member => (IReadOnlyList<string>)
        [member.DisplayName, member.PrimarySmtpAddress ?? "-", member.Type, member.Hidden ? "yes" : "no"]);

    public IEnumerable<string> Footer(DistributionGroupMembers data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} direct member(s) of {1}, {2} of them hidden from the address lists",
            data.Members.Count,
            data.Group,
            data.Members.Count(member => member.Hidden));
    }
}

/// <summary>The table of <c>mailbox organization</c>: a setting per row, then an accepted domain per row.</summary>
internal sealed class OrganizationTableView : ITableView<ExchangeOrganization>
{
    internal static OrganizationTableView Instance { get; } = new();

    private OrganizationTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["setting", "value"];

    public IEnumerable<IReadOnlyList<string>> Rows(ExchangeOrganization data)
    {
        yield return ["name", data.Name ?? "-"];
        yield return ["MailTips", OnOff(data.MailTipsAllTipsEnabled)];
        yield return ["MailTip for external recipients", OnOff(data.MailTipsExternalRecipientsTipsEnabled)];
        yield return ["MailTip for a large audience from", data.MailTipsLargeAudienceThreshold?.ToString(CultureInfo.InvariantCulture) ?? "-"];
        foreach (AcceptedDomainInfo domain in data.AcceptedDomains)
        {
            string type = domain.DomainType ?? "?";
            yield return ["accepted domain", domain.Default == true ? $"{domain.DomainName} ({type}, default)" : $"{domain.DomainName} ({type})"];
        }
    }

    public IEnumerable<string> Footer(ExchangeOrganization data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} accepted domain(s)", data.AcceptedDomains.Count);
    }

    private static string OnOff(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "-",
    };
}
