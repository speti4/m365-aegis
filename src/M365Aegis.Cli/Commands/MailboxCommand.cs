using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis mailbox list|access|forwarders|size</c> (tasks 015 and 016): the mailbox lookups over the Exchange
/// Online sidecar. <c>access</c> takes a mailbox (who holds which right on it) or <c>--user</c> (every mailbox the
/// user holds a right on), never both.
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
            (exo, ct) => MailboxLookups.ListAsync(exo, parsed.GetValue(shared), ct),
            MailboxListTableView.Instance,
            cancellationToken));

        Argument<string?> accessOf = new("mailbox")
        {
            Description = "The mailbox: its address, name or object id.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<string?> user = new("--user") { Description = "The other direction: every mailbox this user (a user principal name) can open or send as." };
        Command access = new("access", "Show who holds FullAccess, Send As and Send on Behalf on a mailbox, or which mailboxes a user holds them on.");
        access.Arguments.Add(accessOf);
        access.Options.Add(user);
        access.SetAction((parsed, cancellationToken) =>
        {
            string? target = parsed.GetValue(accessOf);
            string? holder = parsed.GetValue(user);
            if (string.IsNullOrWhiteSpace(target) == string.IsNullOrWhiteSpace(holder))
            {
                var failure = new AegisFailure(
                    FailureKind.Usage,
                    "give a mailbox or --user, not both and not neither",
                    "`mailbox access finance@contoso.com` lists who can use the mailbox; `mailbox access --user alice@contoso.com` lists what alice can use");
                return Task.FromResult(CommandRunner.Fail(Program.ResolveMode(parsed, json, table), failure));
            }

            return holder is not null
                ? ExchangeRunner.RunAsync(parsed, json, table, profile, (exo, ct) => MailboxLookups.AccessForUserAsync(exo, holder, ct), UserAccessTableView.Instance, cancellationToken)
                : ExchangeRunner.RunAsync(parsed, json, table, profile, (exo, ct) => MailboxLookups.AccessAsync(exo, target!, ct), MailboxAccessTableView.Instance, cancellationToken);
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
        Command size = new("size", "Show the live size and item count of a mailbox, or of every mailbox, the largest first.");
        size.Arguments.Add(sizeOf);
        size.Options.Add(top);
        size.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            (exo, ct) => MailboxLookups.SizeAsync(exo, parsed.GetValue(sizeOf), parsed.GetValue(top), ct),
            MailboxSizeTableView.Instance,
            cancellationToken));

        mailbox.Subcommands.Add(list);
        mailbox.Subcommands.Add(access);
        mailbox.Subcommands.Add(forwarders);
        mailbox.Subcommands.Add(size);
        return mailbox;
    }

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

    public IReadOnlyList<string> Headers => ["right", "holder", "address"];

    public IEnumerable<IReadOnlyList<string>> Rows(MailboxAccess data) =>
        data.Holders.Select(holder => (IReadOnlyList<string>)[MailboxCommand.Right(holder.Right), holder.Holder, holder.Address ?? "-"]);

    public IEnumerable<string> Footer(MailboxAccess data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} grant(s) on {1} <{2}> ({3}); direct grants only, a group is not expanded",
            data.Holders.Count,
            data.Mailbox.DisplayName,
            data.Mailbox.PrimarySmtpAddress,
            data.Mailbox.Type);
    }
}

/// <summary>The table of <c>mailbox access --user</c>: a row per mailbox and right, what was read under the table.</summary>
internal sealed class UserAccessTableView : ITableView<UserAccess>
{
    internal static UserAccessTableView Instance { get; } = new();

    private UserAccessTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["mailbox", "address", "type", "right"];

    public IEnumerable<IReadOnlyList<string>> Rows(UserAccess data) =>
        data.Grants.Select(grant => (IReadOnlyList<string>)
            [grant.Mailbox.DisplayName, grant.Mailbox.PrimarySmtpAddress, grant.Mailbox.Type, MailboxCommand.Right(grant.Right)]);

    public IEnumerable<string> Footer(UserAccess data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} grant(s) for {1}{2} over {3} mailbox(es); direct grants only, a group's members are not expanded",
            data.Grants.Count,
            data.User,
            data.DisplayName is null ? "" : $" ({data.DisplayName})",
            data.MailboxesScanned);
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
        if (data.Failed.Count > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} mailbox(es) could not be read", data.Failed.Count);
        }
    }
}
