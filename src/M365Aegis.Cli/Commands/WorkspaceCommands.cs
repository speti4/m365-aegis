using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Groups;

namespace M365Aegis.Cli.Commands;

/// <summary><c>m365-aegis team list</c> (task 005): every team of the tenant, by name.</summary>
internal static class TeamCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command list = new("list", "List every team of the tenant.");
        list.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            WorkspaceLookups.TeamsAsync,
            TeamTableView.Instance,
            cancellationToken));
        Command team = new("team", "Look up Microsoft Teams teams.");
        team.Subcommands.Add(list);
        return team;
    }
}

/// <summary><c>m365-aegis site list [--search &lt;words&gt;]</c> (task 005): the SharePoint sites a search finds.</summary>
internal static class SiteCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<string?> search = new("--search")
        {
            Description = "Words the sites' names or addresses contain; every site when omitted.",
        };
        Command list = new("list", "List the SharePoint sites the signed-in account can see.");
        list.Options.Add(search);
        list.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => WorkspaceLookups.SitesAsync(graph, parsed.GetValue(search), ct),
            SiteTableView.Instance,
            cancellationToken));
        Command site = new("site", "Look up SharePoint sites.");
        site.Subcommands.Add(list);
        return site;
    }
}

/// <summary>The table of <c>team list</c>.</summary>
internal sealed class TeamTableView : ITableView<IReadOnlyList<TeamInfo>>
{
    internal static TeamTableView Instance { get; } = new();

    private TeamTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["name", "visibility", "id"];

    public IEnumerable<IReadOnlyList<string>> Rows(IReadOnlyList<TeamInfo> data) =>
        data.Select(team => (IReadOnlyList<string>)[team.DisplayName, team.Visibility ?? "-", team.Id]);

    public IEnumerable<string> Footer(IReadOnlyList<TeamInfo> data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} team(s)", data.Count);
    }
}

/// <summary>The table of <c>site list</c>.</summary>
internal sealed class SiteTableView : ITableView<IReadOnlyList<SiteInfo>>
{
    internal static SiteTableView Instance { get; } = new();

    private SiteTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["name", "address", "last modified"];

    public IEnumerable<IReadOnlyList<string>> Rows(IReadOnlyList<SiteInfo> data) => data.Select(site => (IReadOnlyList<string>)
    [
        site.DisplayName,
        site.WebUrl ?? "-",
        site.LastModified is { } modified ? modified.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-",
    ]);

    public IEnumerable<string> Footer(IReadOnlyList<SiteInfo> data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} site(s) the signed-in account can see", data.Count);
    }
}
