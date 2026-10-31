using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis access &lt;user&gt;</c> (task 084): which groups, sites, shared items, mailboxes and calendars one user
/// reaches, from the newest snapshot of the profile (D-011's query path). It reads the local file only (<c>local</c>,
/// no sign-in); without a snapshot it fails with exit code 4 and the take as its hint.
/// </summary>
internal static class AccessCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Argument<string> user = new("user") { Description = "The user: their user principal name, address or object id." };
        Command access = new("access", "Show what one user reaches (groups, sites, shared items, mailboxes, calendars), from the newest snapshot.");
        access.Arguments.Add(user);
        access.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () =>
            {
                string home = AegisHome.Resolve();
                string name = SnapshotStore.ProfileName(home, parsed.GetValue(profile));
                return CommandResult.Success(SnapshotAccess.ForUser(home, name, parsed.GetValue(user) ?? "", DateTimeOffset.UtcNow));
            },
            AccessTableView.Instance));
        return access;
    }
}

/// <summary>
/// The table of <c>access &lt;user&gt;</c>: a row per group, site, shared item, mailbox right and calendar permission,
/// each with what it comes through; the snapshot's time, what reaches everyone and the warnings under it.
/// </summary>
internal sealed class AccessTableView : ITableView<AccessReport>
{
    internal static AccessTableView Instance { get; } = new();

    private AccessTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["section", "what", "how", "through"];

    public IEnumerable<IReadOnlyList<string>> Rows(AccessReport data)
    {
        foreach (AccessGroup group in data.Groups)
        {
            yield return ["group", Name(group.DisplayName), group.Kind ?? "?", group.Direct ? "direct" : string.Join(", ", group.Via)];
        }

        foreach (AccessSite site in data.Sites)
        {
            yield return ["site", site.Name, site.WebUrl ?? "-", $"{site.Role} of {Name(site.Group)}"];
        }

        foreach (AccessSharedItem item in data.SharedItems)
        {
            string how = string.Join(" ", new[] { item.Kind, item.Scope, item.Roles, item.Expires is null ? null : $"until {item.Expires}" }.OfType<string>());
            yield return ["shared", $"{item.Site}: {(item.Path ?? "").TrimEnd('/')}/{item.Item}", how, item.Through ?? "direct"];
        }

        foreach (AccessMailbox mailbox in data.Mailboxes)
        {
            yield return ["mailbox", $"{Name(mailbox.Mailbox)} <{mailbox.Address}>", mailbox.Right, mailbox.Through ?? "direct"];
        }

        foreach (AccessCalendar calendar in data.Calendars)
        {
            yield return ["calendar", $"{Name(calendar.Mailbox)} <{calendar.Address}>", $"{calendar.Folder}: {calendar.Rights}", calendar.Through ?? "direct"];
        }
    }

    public IEnumerable<string> Footer(AccessReport data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} ({1}) from the snapshot {2}, taken {3} UTC, {4:0.#} hours ago",
            Name(data.User.DisplayName),
            data.User.UserPrincipalName ?? data.User.Mail ?? data.User.ObjectId,
            data.Snapshot,
            PlanCommand.When(data.Taken),
            data.AgeHours);
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "reaches everyone: {0} organization-wide link(s), {1} anonymous link(s), {2} calendar(s) open beyond free/busy to every user, {3} to anonymous",
            data.Everyone.OrganizationLinks,
            data.Everyone.AnonymousLinks,
            data.Everyone.DefaultCalendars,
            data.Everyone.AnonymousCalendars);
        foreach (string warning in data.Warnings)
        {
            yield return $"warning: {warning}";
        }
    }

    private static string Name(string? name) => name ?? "?";
}
